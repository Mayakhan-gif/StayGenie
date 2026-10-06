namespace StayGenie.Services;

// Baaki code ko sirf ye interface pata hoga, SMTP ka nahi.
// Kal Mailtrap ki jagah SendGrid lagana ho to sirf nayi class banegi.
public interface IEmailService
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
}