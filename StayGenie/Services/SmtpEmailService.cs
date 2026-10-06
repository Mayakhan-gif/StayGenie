using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace StayGenie.Services;

public class SmtpEmailService(IOptions<EmailOptions> options) : IEmailService
{
    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        var o = options.Value;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(o.FromName, o.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(
            o.Host, o.Port,
            o.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None, ct);

        // Papercut jaise local test server ko login nahi chahiye
        if (!string.IsNullOrEmpty(o.Username))
            await client.AuthenticateAsync(o.Username, o.Password, ct);

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}