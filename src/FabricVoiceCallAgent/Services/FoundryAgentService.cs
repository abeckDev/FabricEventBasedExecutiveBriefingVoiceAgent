using Azure.AI.Projects;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using FabricVoiceCallAgent.Configuration;
using FabricVoiceCallAgent.Models;
using System.Text;

namespace FabricVoiceCallAgent.Services;

public class FoundryAgentService : IDisposable
{
    private readonly FoundrySettings _settings;
    private readonly VoiceAgentSettings _voiceAgentSettings;
    private readonly ILogger<FoundryAgentService> _logger;

    private Agent? _agent;
    private readonly SemaphoreSlim _agentLock = new(1, 1);
    private bool _disposed;

    public FoundryAgentService(
        IOptions<FoundrySettings> settings, 
        IOptions<VoiceAgentSettings> voiceAgentSettings,
        ILogger<FoundryAgentService> logger)
    {
        _settings = settings.Value;
        _voiceAgentSettings = voiceAgentSettings.Value;
        _logger = logger;
    }

    public async Task<string> GenerateExecSummaryAsync(AlertPayload alert)
    {
        try
        {
            var client = CreateAgentsClient();
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
            var client = CreateAgentsClient();
            var agent = await GetOrCreateAgentAsync(client);

            var threadResponse = await client.CreateThreadAsync();
            var thread = threadResponse.Value;

            try
            {
                // Provide context from the exec summary already given to the user
                var contextMessage = $"""
                    Context: The following executive summary was already read to the user:
                    {execSummaryContext}

                    The user may ask follow-up questions about this situation. Use the Data Agent to fetch fresh data as needed.
                    """;
                await client.CreateMessageAsync(thread.Id, MessageRole.User, contextMessage);

                // Add the user's follow-up question
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
                    return "I'm sorry, I was unable to retrieve that information right now. Please try again.";
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

    private AgentsClient CreateAgentsClient()
    {
        // Use connection string if provided, otherwise construct from endpoint
        var connectionString = !string.IsNullOrEmpty(_settings.ProjectConnectionString)
            ? _settings.ProjectConnectionString
            : _settings.ProjectEndpoint;

        TokenCredential credential;
        if (!string.IsNullOrEmpty(_voiceAgentSettings.ManagedIdentityClientId))
        {
            credential = new ManagedIdentityCredential(
                ManagedIdentityId.FromUserAssignedClientId(_voiceAgentSettings.ManagedIdentityClientId));
        }
        else
        {
            credential = new DefaultAzureCredential();
        }

        return new AgentsClient(connectionString, credential);
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
                _logger.LogWarning("DataAgentConnectionId is not configured. Agent will not have access to data.");
            }

            var agentResponse = await client.CreateAgentAsync(
                model: _settings.ModelDeploymentName,
                name: _settings.AgentName,
                instructions: _settings.AgentInstructions,
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

    private string BuildExecSummaryRequest(AlertPayload alert)
    {
        var template = _settings.SummaryRequestTemplate;
        
        return template
            .Replace("{AlertType}", alert.AlertType ?? "N/A")
            .Replace("{SourceId}", alert.SourceId ?? "N/A")
            .Replace("{SourceName}", alert.SourceName ?? "N/A")
            .Replace("{Severity}", alert.Severity ?? "N/A")
            .Replace("{Title}", alert.Title ?? "N/A")
            .Replace("{Description}", alert.Description ?? "N/A")
            .Replace("{Timestamp}", alert.Timestamp?.ToString("u") ?? "N/A")
            .Replace("{Metadata}", alert.GetMetadataSummary());
    }

    private static string BuildFallbackSummary(AlertPayload alert)
    {
        var sb = new StringBuilder();
        sb.Append($"Attention: An alert has been triggered");
        
        if (!string.IsNullOrEmpty(alert.SourceId))
            sb.Append($" for {alert.SourceId}");
        
        if (!string.IsNullOrEmpty(alert.SourceName))
            sb.Append($" at {alert.SourceName}");
        
        sb.Append(". ");
        
        if (!string.IsNullOrEmpty(alert.Title))
            sb.Append($"{alert.Title}. ");
        
        if (!string.IsNullOrEmpty(alert.Description))
            sb.Append($"{alert.Description} ");
        
        sb.Append("Immediate attention is recommended. Please stay on the line to ask follow-up questions.");
        
        return sb.ToString();
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

