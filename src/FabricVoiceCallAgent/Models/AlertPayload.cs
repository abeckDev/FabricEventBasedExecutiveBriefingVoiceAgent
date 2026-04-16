using System.Text.Json.Serialization;

namespace FabricVoiceCallAgent.Models;

/// <summary>
/// Generic alert payload model that can be used with Fabric Data Activator, 
/// custom webhooks, or any other alert source. The model supports both
/// fixed fields for common scenarios and a flexible Metadata dictionary
/// for domain-specific data.
/// </summary>
public class AlertPayload
{
    /// <summary>
    /// Unique identifier for the source of the alert (e.g., machine ID, sensor ID, system name)
    /// </summary>
    [JsonPropertyName("sourceId")]
    public string? SourceId { get; set; }

    /// <summary>
    /// Human-readable name or location of the alert source
    /// </summary>
    [JsonPropertyName("sourceName")]
    public string? SourceName { get; set; }

    /// <summary>
    /// Type of alert (e.g., "Threshold", "Anomaly", "Critical", "Warning")
    /// </summary>
    [JsonPropertyName("alertType")]
    public string? AlertType { get; set; }

    /// <summary>
    /// Severity level (e.g., "Low", "Medium", "High", "Critical")
    /// </summary>
    [JsonPropertyName("severity")]
    public string? Severity { get; set; }

    /// <summary>
    /// Short title or subject of the alert
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Detailed description of the alert condition
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// When the alert was triggered
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset? Timestamp { get; set; }

    /// <summary>
    /// Phone number to call (overrides default from config if provided)
    /// </summary>
    [JsonPropertyName("phoneNumber")]
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// Flexible key-value store for domain-specific data that the Foundry Agent
    /// can use when generating the executive summary. Examples:
    /// - Factory scenario: { "temperature": 87.3, "vibration": 1.45, "orderId": "ORD-001" }
    /// - Healthcare: { "patientId": "P123", "vitals": "elevated", "room": "ICU-5" }
    /// - Retail: { "storeId": "S42", "inventoryLevel": 15, "sku": "ABC-123" }
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>
    /// Gets a string representation of metadata for logging and display
    /// </summary>
    public string GetMetadataSummary()
    {
        if (Metadata == null || Metadata.Count == 0)
            return string.Empty;

        return string.Join(", ", Metadata.Select(kv => $"{kv.Key}={kv.Value}"));
    }
}
