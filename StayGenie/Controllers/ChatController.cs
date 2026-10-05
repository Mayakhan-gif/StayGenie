using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using StayGenie.Services;

[ApiController]
[Route("api/chat")]
public class ChatController(IChatClient chat, ChatTools tools, McpToolProvider mcp) : ControllerBase
{
    public record ChatMsg(string Role, string Content);
    public record ChatRequest(List<ChatMsg> Messages);

    [HttpPost]
    public async Task<IActionResult> Post(ChatRequest req)
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System,
                $"""
                You are StayGenie, a friendly hotel reservation assistant.
                ALWAYS reply in English only, no matter what language the user writes in
                (including Roman Urdu or Hindi). Never use Hindi or Urdu script.
                Keep replies short and clear. Today's date is {today}. Prices are in PKR.

                Rules:
                - When the user asks about rooms, ALWAYS call SearchRooms. Never invent rooms or prices.
                - If guests or dates are missing, ask for them before searching.
                - Present results as a short list: room number, type, price per night, key amenities.
                - Before booking, call CheckAvailability for the chosen room and dates.
                - Before calling CreateReservation, show a summary (room, dates, guests, guest name,
                  total price) and ask the user to confirm. Call CreateReservation ONLY after the user says yes.
                - If CreateReservation or CheckAvailability says the room is not available,
                  tell the user and call SearchRooms again to suggest alternatives.
                - Use the roomId from search results (not the room number) when calling tools.
                - If the user asks whether a booking or reservation exists or its status, ALWAYS call GetReservation. Never guess from the conversation.
                - Only call GetReservation when the user explicitly asks about a reservation, booking status, or confirmation. For greetings or small talk, just respond naturally without calling any tool.
                """)
        };

        foreach (var m in req.Messages)
            messages.Add(new ChatMessage(m.Role == "assistant" ? ChatRole.Assistant : ChatRole.User, m.Content));

        // SearchRooms seedha diya jata hai (vector DB), aur booking wale tools MCP server se mangwaye jate hain
        var mcpTools = await mcp.GetToolsAsync();
        var allTools = new List<AITool> { AIFunctionFactory.Create(tools.SearchRooms) };
        allTools.AddRange(mcpTools);

        var options = new ChatOptions { Tools = allTools };

        var response = await chat.GetResponseAsync(messages, options);
        return Ok(new { reply = response.Text });
    }
}