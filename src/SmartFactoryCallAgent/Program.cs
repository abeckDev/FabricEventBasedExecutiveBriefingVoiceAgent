using Microsoft.Extensions.Options;
using SmartFactoryCallAgent.Configuration;
using SmartFactoryCallAgent.Models;
using SmartFactoryCallAgent.Services;

var builder = WebApplication.CreateBuilder(args);

// Bind configuration
builder.Services.Configure<AcsSettings>(builder.Configuration.GetSection("Acs"));
builder.Services.Configure<OpenAiSettings>(builder.Configuration.GetSection("OpenAi"));
builder.Services.Configure<FoundrySettings>(builder.Configuration.GetSection("Foundry"));
builder.Services.Configure<FabricSettings>(builder.Configuration.GetSection("Fabric"));

// Register services
builder.Services.AddSingleton<CallContextStore>();
builder.Services.AddScoped<FabricDataService>();
builder.Services.AddScoped<FoundryAgentService>();
builder.Services.AddScoped<CallService>();
builder.Services.AddScoped<AudioStreamingHandler>();

// Add controllers
builder.Services.AddControllers();

// Add Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure WebSocket middleware
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

// WebSocket endpoint for bidirectional audio streaming
app.Map("/ws/audio", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    // Extract callConnectionId from query string
    var callConnectionId = context.Request.Query["callConnectionId"].ToString();
    if (string.IsNullOrEmpty(callConnectionId))
    {
        context.Response.StatusCode = 400;
        return;
    }

    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
    var handler = context.RequestServices.GetRequiredService<AudioStreamingHandler>();

    using var cts = new CancellationTokenSource(TimeSpan.FromHours(1));
    await handler.HandleAsync(webSocket, callConnectionId, cts.Token);
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();
