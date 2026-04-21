using FabricDataService.Configuration;
using FabricDataService.Endpoints;
using FabricDataService.Services;

var builder = WebApplication.CreateBuilder(args);

// Bind configuration
builder.Services.Configure<FabricDataServiceSettings>(
    builder.Configuration.GetSection("FabricDataService"));

// Register the primary data query service (Foundry Agent)
builder.Services.AddSingleton<IDataQueryService, FoundryAgentQueryService>();

// Add Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Map endpoints
app.MapAskEndpoints();

app.Run();
