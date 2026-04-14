using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Extensions.Options;
using SmartFactoryCallAgent.Configuration;
using SmartFactoryCallAgent.Models;
using System.Text;

namespace SmartFactoryCallAgent.Services;

public class FoundryAgentService
{
    private readonly FoundrySettings _settings;
    private readonly ILogger<FoundryAgentService> _logger;

    private const string AgentInstructions = """
        You are an AI assistant that helps factory executives quickly understand machine anomalies and production impacts.
        When given factory context data, generate a concise executive summary that:
        1. States the nature of the anomaly (machine, type, severity)
        2. Summarizes current production impact (affected orders, OEE, scrap rate)
        3. Highlights any supply chain risks related to the affected station
        4. Recommends immediate actions (maintenance, order rerouting, etc.)
        Keep the summary to approximately 30 seconds of spoken text (about 75-90 words).
        Use clear, professional language suitable for a factory manager.
        """;

    public FoundryAgentService(IOptions<FoundrySettings> settings, ILogger<FoundryAgentService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<string> GenerateExecSummaryAsync(DataActivatorAlert alert, FactoryContext context)
    {
        try
        {
            // AgentsClient uses AI Projects connection string format:
            // "<endpoint>;<subscription_id>;<resource_group>;<project_name>"
            var client = new AgentsClient(_settings.ProjectEndpoint, new DefaultAzureCredential());

            // Create agent
            var agentResponse = await client.CreateAgentAsync(
                model: "gpt-4o",
                name: "SmartFactoryExecSummaryAgent",
                instructions: AgentInstructions);
            var agent = agentResponse.Value;

            // Create thread
            var threadResponse = await client.CreateThreadAsync();
            var thread = threadResponse.Value;

            try
            {
                // Build the user message with factory context
                var userMessage = BuildContextMessage(alert, context);

                // Add message to thread
                await client.CreateMessageAsync(thread.Id, MessageRole.User, userMessage);

                // Run the agent
                var runResponse = await client.CreateRunAsync(thread, agent);
                var run = runResponse.Value;

                // Poll for completion
                while (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress)
                {
                    await Task.Delay(500);
                    var runRefresh = await client.GetRunAsync(thread.Id, run.Id);
                    run = runRefresh.Value;
                }

                if (run.Status != RunStatus.Completed)
                {
                    _logger.LogWarning("Agent run did not complete successfully. Status: {Status}", run.Status);
                    return BuildFallbackSummary(alert, context);
                }

                // Get the response messages
                var messagesResponse = await client.GetMessagesAsync(thread.Id, run.Id);
                var assistantMessage = messagesResponse.Value.Data
                    .Where(m => m.Role == MessageRole.Agent)
                    .OrderByDescending(m => m.CreatedAt)
                    .FirstOrDefault();

                if (assistantMessage?.ContentItems != null)
                {
                    var textContent = assistantMessage.ContentItems
                        .OfType<MessageTextContent>()
                        .FirstOrDefault();
                    if (textContent != null)
                    {
                        return textContent.Text;
                    }
                }

                return BuildFallbackSummary(alert, context);
            }
            finally
            {
                // Cleanup
                try { await client.DeleteThreadAsync(thread.Id); } catch { }
                try { await client.DeleteAgentAsync(agent.Id); } catch { }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating executive summary via Foundry Agent");
            return BuildFallbackSummary(alert, context);
        }
    }

    private static string BuildContextMessage(DataActivatorAlert alert, FactoryContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Please generate an executive summary for the following machine anomaly:");
        sb.AppendLine();
        sb.AppendLine($"ALERT: Machine {alert.MachineId} at station {alert.StationName}");
        sb.AppendLine($"Vibration: {alert.Vibration}g, Temperature: {alert.Temperature}°C");
        sb.AppendLine($"Alert Time: {alert.Timestamp:u}");
        sb.AppendLine($"Order ID: {alert.OrderId ?? "N/A"}");
        sb.AppendLine();

        if (context.KpiSummary != null)
        {
            sb.AppendLine("CURRENT KPIs (last 8 hours):");
            sb.AppendLine($"  OEE: {context.KpiSummary.AvgOee:P1}, Scrap Rate: {context.KpiSummary.AvgScrapRate:P1}, Uptime: {context.KpiSummary.AvgUptime:P1}");
            sb.AppendLine($"  Units Produced: {context.KpiSummary.TotalUnitsProduced}");
            sb.AppendLine();
        }

        if (context.ActiveOrders.Count > 0)
        {
            sb.AppendLine($"ACTIVE ORDERS ({context.ActiveOrders.Count} orders):");
            foreach (var order in context.ActiveOrders.Take(3))
            {
                sb.AppendLine($"  Order {order.OrderId}: {order.ProductName}, Qty {order.Quantity}, Due {order.DueDate:d}, Status: {order.Status}");
            }
            sb.AppendLine();
        }

        if (context.SupplyRisks.Count > 0)
        {
            sb.AppendLine("SUPPLY RISKS:");
            foreach (var risk in context.SupplyRisks.Take(3))
            {
                sb.AppendLine($"  {risk.Material} from {risk.Supplier}: {risk.RiskLevel} risk - {risk.RiskDescription}");
            }
            sb.AppendLine();
        }

        if (context.TelemetryData.Count > 0)
        {
            sb.AppendLine($"RECENT TELEMETRY (last 15 min, {context.TelemetryData.Count} readings):");
            var latest = context.TelemetryData.First();
            sb.AppendLine($"  Latest: Vibration={latest.Vibration}g, Temp={latest.Temperature}°C, Pressure={latest.Pressure}bar, Status={latest.Status}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string BuildFallbackSummary(DataActivatorAlert alert, FactoryContext context)
    {
        var sb = new StringBuilder();
        sb.Append($"Attention: Machine {alert.MachineId} at {alert.StationName} has triggered a vibration alert at {alert.Vibration}g, ");
        sb.Append($"exceeding the 1.2g threshold. ");

        if (context.ActiveOrders.Count > 0)
        {
            sb.Append($"There are {context.ActiveOrders.Count} active orders potentially affected. ");
        }

        if (context.KpiSummary != null)
        {
            sb.Append($"Current OEE is {context.KpiSummary.AvgOee:P0}. ");
        }

        if (context.SupplyRisks.Any(r => r.RiskLevel is "High" or "Critical"))
        {
            sb.Append("Note: high supply chain risks detected for related materials. ");
        }

        sb.Append("Immediate maintenance inspection is recommended. Please stay on the line to ask follow-up questions.");

        return sb.ToString();
    }
}

