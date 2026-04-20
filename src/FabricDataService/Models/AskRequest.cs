using System.Text.Json.Serialization;

namespace FabricDataService.Models;

/// <summary>
/// HTTP request body for the /ask endpoint.
/// </summary>
public class AskRequest
{
    /// <summary>
    /// The natural language question to send to the Fabric Data Agent.
    /// </summary>
    [JsonPropertyName("question")]
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// Optional alert context that provides grounding for the query.
    /// </summary>
    [JsonPropertyName("alertContext")]
    public Dictionary<string, object>? AlertContext { get; set; }

    /// <summary>
    /// Optional prior summary to provide conversation context for follow-up questions.
    /// </summary>
    [JsonPropertyName("priorSummary")]
    public string? PriorSummary { get; set; }

    /// <summary>
    /// Optional idempotency key to prevent duplicate processing.
    /// </summary>
    [JsonPropertyName("idempotencyKey")]
    public string? IdempotencyKey { get; set; }
}
