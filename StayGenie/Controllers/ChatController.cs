using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using StayGenie.Services;

[ApiController]
[Route("api/chat")]
public class ChatController(IChatClient chat, ChatTools tools, McpToolProvider mcp) : ControllerBase
{
    public record ChatMsg(string Role, string Content);
    public record ChatRequest(List<ChatMsg> Messages);

    // CreateReservation tool ko lapetta hai: userId hamesha token se aata hai,
    // AI jo bhi bheje, wo overwrite ho jata hai.
    private sealed class UserBoundTool(AIFunction inner, int userId) : DelegatingAIFunction(inner)
    {
        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            arguments["userId"] = userId;
            return base.InvokeCoreAsync(arguments, cancellationToken);
        }
    }

    [HttpPost]
    public async Task<IActionResult> Post(ChatRequest req)
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");

        // Login hai to token se user ki id, warna 0 (bina login)
        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

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
                - Never set the userId argument of CreateReservation; the system fills it in.
                - If CreateReservation or CheckAvailability says the room is not available,
                  tell the user and call SearchRooms again to suggest alternatives.
                - Use the roomId from search results (not the room number) when calling tools.
                - If the user asks whether a booking or reservation exists or its status, ALWAYS call GetReservation with BOTH the reservation ID and the guest name. If either one is missing, ask the user for it first. Never guess the ID or the name, and never guess from the conversation.
                - Only call GetReservation when the user explicitly asks about a reservation, booking status, or confirmation. For greetings or small talk, just respond naturally without calling any tool.
                """)
        };

        foreach (var m in req.Messages)
            messages.Add(new ChatMessage(m.Role == "assistant" ? ChatRole.Assistant : ChatRole.User, m.Content));

        // SearchRooms seedha diya jata hai (vector DB), aur booking wale tools MCP server se mangwaye jate hain
        var mcpTools = await mcp.GetToolsAsync();
        var allTools = new List<AITool> { AIFunctionFactory.Create(tools.SearchRooms) };

        foreach (var t in mcpTools)
        {
            var isCreate = t is AIFunction f
                && f.Name.Replace("_", "").Equals("CreateReservation", StringComparison.OrdinalIgnoreCase);

            allTools.Add(isCreate ? new UserBoundTool((AIFunction)t, userId) : t);
        }

        var options = new ChatOptions { Tools = allTools };

        var response = await chat.GetResponseAsync(messages, options);
        return Ok(new { reply = response.Text });
    }
}