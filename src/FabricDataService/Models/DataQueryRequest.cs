namespace FabricDataService.Models;

/// <summary>
/// Domain-level query request, decoupled from the HTTP transport DTO.
/// Used by IDataQueryService implementations.
/// </summary>
public class DataQueryRequest
{
    /// <summary>
    /// The primary question/query to send to the data backend.
    /// </summary>
    public required string Question { get; init; }

    /// <summary>
    /// Optional context messages to prepend (e.g., alert context, prior summary).
    /// </summary>
    public IReadOnlyList<string> ContextMessages { get; init; } = [];

    /// <summary>
    /// Correlation ID for tracing across services.
    /// </summary>
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");
}
