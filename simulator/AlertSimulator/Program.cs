using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using System.Text;
using System.Text.Json;

namespace AlertSimulator;

/// <summary>
/// Generic alert simulator that can be customized for different Fabric demo scenarios.
/// Sends events to Event Hub that can trigger Data Activator alerts.
/// </summary>
class Program
{
    // Update these values before running
    private const string EventHubConnectionString = "<YOUR_EVENT_HUB_CONNECTION_STRING>";
    private const string EventHubName = "<YOUR_EVENT_HUB_NAME>";

    // Simulation configuration - customize for your demo scenario
    private static readonly SimulationConfig Config = new()
    {
        // Source identifiers (e.g., machine IDs, sensor IDs, device IDs)
        SourceIds = new[] { "SOURCE-001", "SOURCE-002", "SOURCE-003", "SOURCE-004" },
        
        // Location/station names
        SourceNames = new[] { "Location-A", "Location-B", "Location-C", "Location-D" },
        
        // Trigger anomaly every N events on the first source
        AnomalyIntervalEvents = 50,
        
        // Delay between events in milliseconds
        EventDelayMs = 1000
    };

    private static readonly Random Random = new();

    static async Task Main(string[] args)
    {
        Console.WriteLine("Fabric Voice Call Agent - Alert Simulator");
        Console.WriteLine("==========================================");
        Console.WriteLine($"Sending events to: {EventHubName}");
        Console.WriteLine("Press Ctrl+C to stop.");
        Console.WriteLine();

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            Console.WriteLine("\nStopping simulator...");
            e.Cancel = true;
            cts.Cancel();
        };

        await using var producer = new EventHubProducerClient(EventHubConnectionString, EventHubName);

        var eventCounter = 0;
        var anomalyTriggered = false;

        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                var sourceIndex = Random.Next(Config.SourceIds.Length);
                var sourceId = Config.SourceIds[sourceIndex];
                var sourceName = Config.SourceNames[sourceIndex];

                // Trigger anomaly every N events on first source for demo purposes
                var triggerAnomaly = eventCounter > 0 && 
                                     eventCounter % Config.AnomalyIntervalEvents == 0 && 
                                     !anomalyTriggered;
                if (triggerAnomaly)
                {
                    anomalyTriggered = true;
                    Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] *** TRIGGERING ANOMALY on {Config.SourceIds[0]} ***");
                }

                // Generate telemetry event
                var telemetryEvent = GenerateTelemetryEvent(sourceId, sourceName, triggerAnomaly && sourceId == Config.SourceIds[0]);
                await SendEventAsync(producer, telemetryEvent, cts.Token);

                eventCounter++;

                if (eventCounter % 10 == 0)
                {
                    Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] Sent {eventCounter} events");
                }

                await Task.Delay(Config.EventDelayMs, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
            }
        }

        Console.WriteLine($"Simulator stopped. Total events sent: {eventCounter}");
    }

    private static object GenerateTelemetryEvent(string sourceId, string sourceName, bool anomaly)
    {
        // Generate a generic telemetry event with common metrics
        // Customize these for your specific demo scenario
        var baseValue1 = anomaly ? 1.3 + Random.NextDouble() * 0.5 : 0.5 + Random.NextDouble() * 0.4;
        var baseValue2 = anomaly ? 85 + Random.NextDouble() * 20 : 60 + Random.NextDouble() * 15;

        return new
        {
            EventType = "Telemetry",
            SourceId = sourceId,
            SourceName = sourceName,
            // Add your domain-specific metrics here
            Metric1 = Math.Round(baseValue1, 2),
            Metric2 = Math.Round(baseValue2, 1),
            Metric3 = Math.Round(2.5 + Random.NextDouble() * 1.5, 2),
            Status = anomaly ? "Warning" : (Random.Next(2) == 0 ? "Running" : "Idle"),
            Timestamp = DateTime.UtcNow.ToString("o")
        };
    }

    private static async Task SendEventAsync(EventHubProducerClient producer, object eventData, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(eventData);
        var eventDataBytes = new EventData(Encoding.UTF8.GetBytes(json));
        eventDataBytes.Properties["EventType"] = "Telemetry";

        using var batch = await producer.CreateBatchAsync(ct);
        if (!batch.TryAdd(eventDataBytes))
        {
            throw new InvalidOperationException("Event too large for batch");
        }
        await producer.SendAsync(batch, ct);
    }
}

/// <summary>
/// Configuration for the simulation. Customize for your demo scenario.
/// </summary>
public class SimulationConfig
{
    public string[] SourceIds { get; set; } = Array.Empty<string>();
    public string[] SourceNames { get; set; } = Array.Empty<string>();
    public int AnomalyIntervalEvents { get; set; } = 50;
    public int EventDelayMs { get; set; } = 1000;
}
