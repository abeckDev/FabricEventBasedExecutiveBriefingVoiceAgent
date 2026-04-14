using System.Text.Json.Serialization;

namespace SmartFactoryCallAgent.Models;

public class DataActivatorAlert
{
    [JsonPropertyName("machineId")]
    public string? MachineId { get; set; }

    [JsonPropertyName("stationName")]
    public string? StationName { get; set; }

    [JsonPropertyName("vibration")]
    public double? Vibration { get; set; }

    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset? Timestamp { get; set; }

    [JsonPropertyName("orderId")]
    public string? OrderId { get; set; }

    [JsonPropertyName("alertType")]
    public string? AlertType { get; set; }

    [JsonPropertyName("severity")]
    public string? Severity { get; set; }
}
