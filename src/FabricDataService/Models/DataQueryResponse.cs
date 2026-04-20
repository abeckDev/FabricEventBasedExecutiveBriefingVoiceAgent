namespace FabricDataService.Models;

/// <summary>
/// Domain-level query response, decoupled from the HTTP transport DTO.
/// </summary>
public class DataQueryResponse
{
    public required string Answer { get; init; }
    public string BackendUsed { get; init; } = "foundry-agent";
    public long DurationMs { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public int ToolCallsCount { get; init; }
}
