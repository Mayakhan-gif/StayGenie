using System.ClientModel;
using System.Text;
using System.Threading.RateLimiting;
using Hangfire;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using OpenAI;
using Qdrant.Client;
using StayGenie.Data;
using StayGenie.Services;

var builder = WebApplication.CreateBuilder(args);

// Groq chat client
IChatClient chatClient = new OpenAIClient(
        new ApiKeyCredential(builder.Configuration["Groq:ApiKey"]!),
        new OpenAIClientOptions { Endpoint = new Uri("https://api.groq.com/openai/v1") })
    .GetChatClient("openai/gpt-oss-120b")
    .AsIChatClient()
    .AsBuilder()
    .UseFunctionInvocation()
    .Build();

builder.Services.AddSingleton(chatClient);

// SQL Server (PMS database)
builder.Services.AddDbContext<HotelDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Hotel")));

// Hangfire: wohi "Hotel" connection string jo EF Core use karta hai
builder.Services.AddHangfire(cfg => cfg
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(builder.Configuration.GetConnectionString("Hotel")));
builder.Services.AddHangfireServer();

// Email: settings "Email" section se, sender ek interface ke peeche
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.AddScoped<IEmailService, SmtpEmailService>();

// Payment: Adyen ki settings "Adyen" section se, keys user-secrets se
builder.Services.Configure<AdyenOptions>(builder.Configuration.GetSection("Adyen"));
builder.Services.AddHttpClient<IPaymentService, AdyenPaymentService>();

// Background jobs
builder.Services.AddScoped<ReservationCleanupJob>();
builder.Services.AddScoped<ReservationEmailJob>();

// Qdrant (vector DB)
builder.Services.AddSingleton(new QdrantClient("localhost", 6334));
builder.Services.AddScoped<RoomSearchService>();
builder.Services.AddScoped<ChatTools>();

// MCP client: chatbot isi se MCP server (/mcp) ke booking tools mangwata hai
builder.Services.AddSingleton<McpToolProvider>();

// HttpClient jo ReservationTools (MCP server ke andar) ko PMS Reservation API se jodta hai.
// X-Internal-Key: sirf hamari apni app ye header bhej sakti hai, is se PMS ko pata chalta hai
// ke UserId par bharosa kar sakte hain (bahar wala doosre ki UserId nahi bhej sakta).
builder.Services.AddHttpClient("Pms", c =>
{
    c.BaseAddress = new Uri("https://localhost:7115");
    var internalKey = builder.Configuration["Internal:ApiKey"];
    if (!string.IsNullOrWhiteSpace(internalKey))
        c.DefaultRequestHeaders.Add("X-Internal-Key", internalKey);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => true
});
builder.Services.AddScoped<ReservationTools>();

// MCP server (/mcp endpoint) — isi ke andar ReservationTools chalte hain
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<ReservationTools>();

// Authentication (JWT)
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is not configured.");

builder.Services.AddSingleton<JwtTokenService>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = "StayGenie",
            ValidAudience = "StayGenieUsers",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Errors: ek jaisa format (ProblemDetails), aur unhandled errors ka safe jawab
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Rate limit: ek IP se ek minute mein limited requests (brute force aur quota abuse se bachao)
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));

    o.AddPolicy("chat", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
});

// Swagger + "Authorize" button (JWT token paste karne ke liye)
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Login ke baad mila hua token yahan paste karo"
    });

    c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

// CORS: sirf frontend ke origins (appsettings mein "Cors:Origins" se, warna local Vite)
var allowedOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
    ?? new[] { "http://localhost:5173" };
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Development mein stack trace wala page khud aata hai, baqi jagah GlobalExceptionHandler
app.UseExceptionHandler();
app.UseStatusCodePages();

// Startup par rooms ko Qdrant mein index karo
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<RoomSearchService>().IndexRoomsAsync();
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseRateLimiter();

// Order zaroori hai: pehle Authentication, phir Authorization
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapMcp("/mcp");

// Hangfire dashboard: sirf Development mein, sirf apni machine se
if (app.Environment.IsDevelopment())
{
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = new[] { new Hangfire.Dashboard.LocalRequestsOnlyAuthorizationFilter() }
    });
}

// Recurring jobs
RecurringJob.AddOrUpdate<ReservationCleanupJob>(
    "expire-pending-reservations", j => j.ExpirePendingAsync(), "*/5 * * * *");

// Roz subah 9 baje Pakistan time (Hangfire UTC use karta hai, UTC+5 ke hisaab se 04:00 UTC)
RecurringJob.AddOrUpdate<ReservationEmailJob>(
    "send-checkin-reminders", j => j.SendRemindersAsync(), "0 4 * * *");

app.Run();