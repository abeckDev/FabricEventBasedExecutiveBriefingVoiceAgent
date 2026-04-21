using System.Text.Json.Serialization;

namespace FabricDataService.Models;

/// <summary>
/// HTTP response body from the /ask endpoint.
/// </summary>
public class AskResponse
{
    [JsonPropertyName("answer")]
    public string Answer { get; set; } = string.Empty;

    [JsonPropertyName("metadata")]
    public AskResponseMetadata Metadata { get; set; } = new();
}

public class AskResponseMetadata
{
    [JsonPropertyName("backendUsed")]
    public string BackendUsed { get; set; } = "foundry-agent";

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }

    [JsonPropertyName("correlationId")]
    public string CorrelationId { get; set; } = string.Empty;

    [JsonPropertyName("toolCallsCount")]
    public int ToolCallsCount { get; set; }
}
