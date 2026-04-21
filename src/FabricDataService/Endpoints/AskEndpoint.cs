using FabricDataService.Models;
using FabricDataService.Services;

namespace FabricDataService.Endpoints;

public static class AskEndpoint
{
    public static void MapAskEndpoints(this WebApplication app)
    {
        app.MapPost("/ask", HandleAskAsync)
            .WithName("Ask")
            .WithOpenApi()
            .Produces<AskResponse>(200)
            .Produces(400)
            .Produces(500);

        app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }))
            .WithName("HealthLive")
            .ExcludeFromDescription();

        app.MapGet("/health/ready", HandleReadinessAsync)
            .WithName("HealthReady")
            .ExcludeFromDescription();
    }

    private static async Task<IResult> HandleAskAsync(
        AskRequest request,
        IDataQueryService queryService,
        ILogger<AskRequest> logger,
        HttpContext httpContext)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return Results.BadRequest(new { error = "The 'question' field is required." });
        }

        var correlationId = !string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? request.IdempotencyKey.Trim()
            : Guid.NewGuid().ToString("N");

        logger.LogInformation(
            "Received /ask request. Question={QuestionPreview}, CorrelationId={CorrelationId}",
            request.Question[..Math.Min(request.Question.Length, 100)], correlationId);

        // Build domain request from HTTP DTO
        var contextMessages = new List<string>();

        if (!string.IsNullOrWhiteSpace(request.PriorSummary))
        {
            contextMessages.Add(
                $"Context: The following summary was previously generated:\n{request.PriorSummary}\n\n" +
                "The user may ask follow-up questions about this situation. Use the Data Agent to fetch fresh data as needed.");
        }

        if (request.AlertContext is { Count: > 0 })
        {
            var alertSummary = string.Join(", ", request.AlertContext.Select(kv => $"{kv.Key}={kv.Value}"));
            contextMessages.Add($"Alert context: {alertSummary}");
        }

        var domainRequest = new DataQueryRequest
        {
            Question = request.Question,
            ContextMessages = contextMessages,
            CorrelationId = correlationId
        };

        DataQueryResponse domainResponse;
        try
        {
            domainResponse = await queryService.QueryAsync(
                domainRequest, httpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Unhandled error in query service. CorrelationId={CorrelationId}", correlationId);
            return Results.Json(
                new { error = "An unexpected error occurred while querying the data service.", correlationId },
                statusCode: 500);
        }

        var response = new AskResponse
        {
            Answer = domainResponse.Answer,
            Metadata = new AskResponseMetadata
            {
                BackendUsed = domainResponse.BackendUsed,
                DurationMs = domainResponse.DurationMs,
                CorrelationId = domainResponse.CorrelationId,
                ToolCallsCount = domainResponse.ToolCallsCount
            }
        };

        return Results.Ok(response);
    }

    private static async Task<IResult> HandleReadinessAsync(IDataQueryService queryService)
    {
        var error = await queryService.CheckReadinessAsync();
        if (error != null)
        {
            return Results.Json(
                new { status = "not_ready", reason = error },
                statusCode: 503);
        }

        return Results.Ok(new { status = "ready" });
    }
}
