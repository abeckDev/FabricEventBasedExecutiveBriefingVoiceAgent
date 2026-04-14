using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Extensions.Options;
using SmartFactoryCallAgent.Configuration;
using SmartFactoryCallAgent.Models;
using System.Text;

namespace SmartFactoryCallAgent.Services;

public class FoundryAgentService : IDisposable
{
    private readonly FoundrySettings _settings;
    private readonly ILogger<FoundryAgentService> _logger;

    private Agent? _agent;
    private readonly SemaphoreSlim _agentLock = new(1, 1);
    private bool _disposed;

    private const string AgentInstructions = """
        You are a Smart Factory AI assistant that helps factory executives quickly understand machine anomalies and production impacts.
        You have access to the live factory database through a connected Fabric Data Agent.
        When asked to generate an executive summary or answer questions, query the Data Agent for up-to-date information.
        Use clear, professional language suitable for a factory manager.
        Keep spoken summaries to approximately 30 seconds (~75-90 words).
        """;

    public FoundryAgentService(IOptions<FoundrySettings> settings, ILogger<FoundryAgentService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<string> GenerateExecSummaryAsync(DataActivatorAlert alert)
    {
        try
        {
            var client = new AgentsClient(_settings.ProjectEndpoint, new DefaultAzureCredential());
            var agent = await GetOrCreateAgentAsync(client);

            var threadResponse = await client.CreateThreadAsync();
            var thread = threadResponse.Value;

            try
            {
                var userMessage = BuildExecSummaryRequest(alert);
                await client.CreateMessageAsync(thread.Id, MessageRole.User, userMessage);

                var runResponse = await client.CreateRunAsync(thread, agent);
                var run = runResponse.Value;

                while (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress)
                {
                    await Task.Delay(500);
                    var runRefresh = await client.GetRunAsync(thread.Id, run.Id);
                    run = runRefresh.Value;
                }

                if (run.Status != RunStatus.Completed)
                {
                    _logger.LogWarning("Agent run did not complete successfully. Status: {Status}", run.Status);
                    return BuildFallbackSummary(alert);
                }

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
                        return textContent.Text;
                }

                return BuildFallbackSummary(alert);
            }
            finally
            {
                try { await client.DeleteThreadAsync(thread.Id); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to delete thread {ThreadId} during cleanup", thread.Id); }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating executive summary via Foundry Agent");
            return BuildFallbackSummary(alert);
        }
    }

    public async Task<string> AnswerFollowUpAsync(string question, string execSummaryContext)
    {
        try
        {
            var client = new AgentsClient(_settings.ProjectEndpoint, new DefaultAzureCredential());
            var agent = await GetOrCreateAgentAsync(client);

            var threadResponse = await client.CreateThreadAsync();
            var thread = threadResponse.Value;

            try
            {
                // Provide context from the exec summary already given to the manager
                var contextMessage = $"""
                    Context: The following executive summary was already read to the factory manager:
                    {execSummaryContext}

                    The manager may ask follow-up questions about this situation. Use the Data Agent to fetch fresh data as needed.
                    """;
                await client.CreateMessageAsync(thread.Id, MessageRole.User, contextMessage);

                // Add the manager's follow-up question
                await client.CreateMessageAsync(thread.Id, MessageRole.User, question);

                var runResponse = await client.CreateRunAsync(thread, agent);
                var run = runResponse.Value;

                while (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress)
                {
                    await Task.Delay(500);
                    var runRefresh = await client.GetRunAsync(thread.Id, run.Id);
                    run = runRefresh.Value;
                }

                if (run.Status != RunStatus.Completed)
                {
                    _logger.LogWarning("Follow-up agent run did not complete successfully. Status: {Status}", run.Status);
                    return "I'm sorry, I was unable to retrieve that information right now. Please try again or contact the operations team directly.";
                }

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
                        return textContent.Text;
                }

                return "I'm sorry, I could not generate a response to your question.";
            }
            finally
            {
                try { await client.DeleteThreadAsync(thread.Id); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to delete thread {ThreadId} during cleanup", thread.Id); }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error answering follow-up question via Foundry Agent");
            return "I'm sorry, an error occurred while retrieving that information.";
        }
    }

    private async Task<Agent> GetOrCreateAgentAsync(AgentsClient client, CancellationToken cancellationToken = default)
    {
        if (_agent != null)
            return _agent;

        await _agentLock.WaitAsync(cancellationToken);
        try
        {
            if (_agent != null)
                return _agent;

            var toolDefinitions = new List<ToolDefinition>();

            if (!string.IsNullOrEmpty(_settings.DataAgentConnectionId))
            {
                var connectionList = new ToolConnectionList();
                connectionList.ConnectionList.Add(new ToolConnection(_settings.DataAgentConnectionId));
                toolDefinitions.Add(new MicrosoftFabricToolDefinition(connectionList));
                _logger.LogInformation("Configured Foundry Agent with Fabric Data Agent connection: {ConnectionId}",
                    _settings.DataAgentConnectionId);
            }
            else
            {
                _logger.LogWarning("DataAgentConnectionId is not configured. Agent will not have access to factory data.");
            }

            var agentResponse = await client.CreateAgentAsync(
                model: _settings.ModelDeploymentName,
                name: "SmartFactoryAssistant",
                instructions: AgentInstructions,
                tools: toolDefinitions);

            _agent = agentResponse.Value;
            _logger.LogInformation("Created Foundry Agent: {AgentId}", _agent.Id);
            return _agent;
        }
        finally
        {
            _agentLock.Release();
        }
    }

    private static string BuildExecSummaryRequest(DataActivatorAlert alert)
    {
        var sb = new StringBuilder();
        sb.AppendLine("A machine anomaly alert has been triggered. Please:");
        sb.AppendLine("1. Query the Data Agent for recent telemetry for the alerting machine (last 15 minutes)");
        sb.AppendLine("2. Query for active production orders at the affected station");
        sb.AppendLine("3. Query for current production KPIs");
        sb.AppendLine("4. Query for any supply chain risks");
        sb.AppendLine("5. Generate a concise ~30-second spoken executive summary for the factory manager covering: the anomaly, production impact, and recommended immediate actions.");
        sb.AppendLine();
        sb.AppendLine("Alert details:");
        sb.AppendLine($"  Machine ID: {alert.MachineId}");
        sb.AppendLine($"  Station: {alert.StationName}");
        sb.AppendLine($"  Vibration: {alert.Vibration}g");
        sb.AppendLine($"  Temperature: {alert.Temperature}°C");
        sb.AppendLine($"  Timestamp: {alert.Timestamp:u}");
        sb.AppendLine($"  Order ID: {alert.OrderId ?? "N/A"}");
        return sb.ToString();
    }

    private static string BuildFallbackSummary(DataActivatorAlert alert)
    {
        return $"Attention: Machine {alert.MachineId} at {alert.StationName} has triggered a vibration alert at {alert.Vibration}g, " +
               "exceeding the threshold. Immediate maintenance inspection is recommended. " +
               "Please stay on the line to ask follow-up questions.";
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _agentLock.Dispose();
            _disposed = true;
        }
    }
}

