using FabricDataService.Models;

namespace FabricDataService.Services;

/// <summary>
/// Stub for future direct Kusto/Fabric query fallback.
/// Bypasses the Foundry Agent and queries Fabric/Kusto directly,
/// then uses an LLM only for summarization.
/// </summary>
public class DirectKustoQueryService : IDataQueryService
{
    private readonly ILogger<DirectKustoQueryService> _logger;

    public DirectKustoQueryService(ILogger<DirectKustoQueryService> logger)
    {
        _logger = logger;
    }

    public Task<DataQueryResponse> QueryAsync(
        DataQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("DirectKustoQueryService is a stub and not yet implemented.");

        return Task.FromResult(new DataQueryResponse
        {
            Answer = "Direct Kusto query fallback is not yet implemented.",
            BackendUsed = "direct-kusto-stub",
            DurationMs = 0,
            CorrelationId = request.CorrelationId,
            ToolCallsCount = 0
        });
    }

    public Task<string?> CheckReadinessAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>("DirectKustoQueryService is not yet implemented.");
    }
}
