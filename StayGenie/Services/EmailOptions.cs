namespace StayGenie.Services;

// appsettings ke "Email" section se bharta hai
public class EmailOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 25;
    public bool UseSsl { get; set; } = false;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";   // user-secrets ya environment variable se, file mein nahi
    public string FromAddress { get; set; } = "noreply@staygenie.local";
    public string FromName { get; set; } = "StayGenie";
}