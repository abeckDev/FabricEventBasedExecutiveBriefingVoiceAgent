using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using System.Text;
using System.Text.Json;

namespace SmartFactorySimulator;

class Program
{
    // Update these values before running
    private const string EventHubConnectionString = "<YOUR_EVENT_HUB_CONNECTION_STRING>";
    private const string EventHubName = "<YOUR_EVENT_HUB_NAME>";

    private static readonly Random Random = new();

    private static readonly string[] MachineIds = { "MACHINE-001", "MACHINE-002", "MACHINE-003", "MACHINE-004" };
    private static readonly string[] StationNames = { "Assembly-A", "Assembly-B", "Welding-C", "Painting-D" };
    private static readonly string[] OrderIds = { "ORD-2024-001", "ORD-2024-002", "ORD-2024-003", "ORD-2024-004" };
    private static readonly string[] EventTypes = { "CycleStart", "CycleEnd", "QualityCheck", "MaintenanceAlert", "ToolChange" };
    private static readonly string[] ProductNames = { "Chassis-v2", "Bracket-A12", "Gear-Set-7", "Panel-XR" };
    private static readonly string[] Statuses = { "Running", "Idle", "Warning", "Error" };

    static async Task Main(string[] args)
    {
        Console.WriteLine("Smart Factory Event Simulator");
        Console.WriteLine("==============================");
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
                var machineIndex = Random.Next(MachineIds.Length);
                var machineId = MachineIds[machineIndex];
                var stationName = StationNames[machineIndex];
                var orderId = OrderIds[Random.Next(OrderIds.Length)];

                // Trigger anomaly every 50 events on MACHINE-001 for demo purposes
                var triggerAnomaly = eventCounter > 0 && eventCounter % 50 == 0 && !anomalyTriggered;
                if (triggerAnomaly)
                {
                    anomalyTriggered = true;
                    Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] *** TRIGGERING ANOMALY on MACHINE-001 ***");
                }

                // Generate telemetry event
                var telemetryEvent = GenerateTelemetryEvent(machineId, stationName, orderId, triggerAnomaly && machineId == "MACHINE-001");
                await SendEventAsync(producer, telemetryEvent, cts.Token);

                // Generate assembly event
                var assemblyEvent = GenerateAssemblyEvent(stationName, orderId);
                await SendEventAsync(producer, assemblyEvent, cts.Token);

                // Occasionally generate KPI event
                if (eventCounter % 5 == 0)
                {
                    var kpiEvent = GenerateKpiEvent(stationName);
                    await SendEventAsync(producer, kpiEvent, cts.Token);
                }

                eventCounter++;

                if (eventCounter % 10 == 0)
                {
                    Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] Sent {eventCounter} events");
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
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

    private static object GenerateTelemetryEvent(string machineId, string stationName, string orderId, bool anomaly)
    {
        var baseVibration = anomaly ? 1.3 + Random.NextDouble() * 0.5 : 0.5 + Random.NextDouble() * 0.4;
        var baseTemp = anomaly ? 85 + Random.NextDouble() * 20 : 60 + Random.NextDouble() * 15;

        return new
        {
            EventType = "MachineTelemetry",
            MachineId = machineId,
            StationName = stationName,
            OrderId = orderId,
            Vibration = Math.Round(baseVibration, 2),
            Temperature = Math.Round(baseTemp, 1),
            Pressure = Math.Round(2.5 + Random.NextDouble() * 1.5, 2),
            Status = anomaly ? "Warning" : Statuses[Random.Next(2)], // Warning or Running/Idle
            Timestamp = DateTime.UtcNow.ToString("o")
        };
    }

    private static object GenerateAssemblyEvent(string stationName, string orderId)
    {
        return new
        {
            EventType = "AssemblyEvent",
            StationName = stationName,
            OrderId = orderId,
            AssemblyEventType = EventTypes[Random.Next(EventTypes.Length)],
            Details = $"Event at {stationName} for order {orderId}",
            Timestamp = DateTime.UtcNow.ToString("o")
        };
    }

    private static object GenerateKpiEvent(string stationName)
    {
        return new
        {
            EventType = "ProductionKPI",
            StationName = stationName,
            OEE = Math.Round(0.70 + Random.NextDouble() * 0.25, 3),
            ScrapRate = Math.Round(Random.NextDouble() * 0.05, 3),
            Uptime = Math.Round(0.85 + Random.NextDouble() * 0.14, 3),
            UnitsProduced = Random.Next(50, 200),
            Timestamp = DateTime.UtcNow.ToString("o")
        };
    }

    private static async Task SendEventAsync(EventHubProducerClient producer, object eventData, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(eventData);
        var eventDataBytes = new EventData(Encoding.UTF8.GetBytes(json));
        eventDataBytes.Properties["EventType"] = eventData.GetType().GetProperty("EventType")?.GetValue(eventData)?.ToString();

        using var batch = await producer.CreateBatchAsync(ct);
        if (!batch.TryAdd(eventDataBytes))
        {
            throw new InvalidOperationException("Event too large for batch");
        }
        await producer.SendAsync(batch, ct);
    }
}
