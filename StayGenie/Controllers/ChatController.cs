using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    [EnableRateLimiting("chat")]
    public async Task<IActionResult> Post(ChatRequest req)
    {
        // Bohat lambi ya bohat zyada messages wali requests ko pehle hi rok do
        if (req.Messages is null || req.Messages.Count == 0 || req.Messages.Count > 30
            || req.Messages.Any(m => string.IsNullOrWhiteSpace(m.Content) || m.Content.Length > 2000))
            return BadRequest("Invalid chat request.");

        var today = BookingRules.Today().ToString("yyyy-MM-dd");

        // Login hai to token se user ki id, warna 0 (bina login)
        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

        // Login user ki email server khud account se le leta hai, bina login se poochni parti hai
        var emailRule = userId > 0
            ? "- The user is logged in. Do NOT ask for an email address; the system uses their account email."
            : "- The user is not logged in. Before showing the booking summary, ask for the guest's email address (for the confirmation email) and pass it as guestEmail when calling CreateReservation.";

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
                {emailRule}
                - CreateReservation does NOT confirm the booking. It returns PENDING_PAYMENT and a payment link.
                  Give the user the payment link exactly as returned, with the reservation ID, and say the room is
                  held for the stated minutes and the booking is confirmed only after payment.
                  NEVER say the booking is confirmed or booked until a lookup shows status Confirmed.
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