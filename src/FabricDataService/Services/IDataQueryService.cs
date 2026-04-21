using FabricDataService.Models;

namespace FabricDataService.Services;

/// <summary>
/// Abstraction for querying data from Fabric.
/// Implementations can use Foundry Agent, direct Kusto, or other backends.
/// </summary>
public interface IDataQueryService
{
    /// <summary>
    /// Send a natural language query and receive a grounded answer.
    /// </summary>
    Task<DataQueryResponse> QueryAsync(DataQueryRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verify that the backend is configured and reachable.
    /// Returns null if healthy, or an error message if not.
    /// </summary>
    Task<string?> CheckReadinessAsync(CancellationToken cancellationToken = default);
}
