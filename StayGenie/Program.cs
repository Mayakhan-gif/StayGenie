using System.ClientModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OpenAI;
using Qdrant.Client;
using StayGenie.Data;
using StayGenie.Services;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
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

// Qdrant (vector DB)
builder.Services.AddSingleton(new QdrantClient("localhost", 6334));
builder.Services.AddScoped<RoomSearchService>();
builder.Services.AddScoped<ChatTools>();

// MCP client: chatbot isi se MCP server (/mcp) ke booking tools mangwata hai
builder.Services.AddSingleton<McpToolProvider>();

// HttpClient jo ReservationTools (MCP server ke andar) ko PMS Reservation API se jodta hai
builder.Services.AddHttpClient("Pms", c =>
{
    c.BaseAddress = new Uri("https://localhost:7115");
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
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Startup par rooms ko Qdrant mein index karo
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<RoomSearchService>().IndexRoomsAsync();
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapMcp("/mcp");
app.Run();