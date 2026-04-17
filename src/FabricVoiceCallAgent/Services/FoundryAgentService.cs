using Azure.AI.Agents.Persistent;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using FabricVoiceCallAgent.Configuration;
using FabricVoiceCallAgent.Models;
using System.Text;

namespace FabricVoiceCallAgent.Services;

/// <summary>
/// Talks to an Azure AI Foundry (account-based) project via the modern
/// <see cref="PersistentAgentsClient"/> and a Microsoft Fabric Data Agent tool.
/// </summary>
public class FoundryAgentService : IDisposable
{
    private readonly FoundrySettings _settings;
    private readonly VoiceAgentSettings _voiceAgentSettings;
    private readonly ILogger<FoundryAgentService> _logger;

    private PersistentAgentsClient? _client;
    private PersistentAgent? _agent;
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
            var client = GetClient();
            var agent = await GetOrCreateAgentAsync(client);

            var userMessage = BuildExecSummaryRequest(alert);
            var result = await RunAgentAsync(client, agent, userMessage);
            return result ?? BuildFallbackSummary(alert);
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
            var client = GetClient();
            var agent = await GetOrCreateAgentAsync(client);

            var primer = $"""
                Context: The following executive summary was already read to the user:
                {execSummaryContext}

                The user may ask follow-up questions about this situation. Use the Data Agent to fetch fresh data as needed.
                """;

            var result = await RunAgentAsync(client, agent, primer, question);
            return result ?? "I'm sorry, I could not generate a response to your question.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error answering follow-up question via Foundry Agent");
            return "I'm sorry, an error occurred while retrieving that information.";
        }
    }

    private async Task<string?> RunAgentAsync(
        PersistentAgentsClient client,
        PersistentAgent agent,
        params string[] userMessages)
    {
        var thread = (await client.Threads.CreateThreadAsync()).Value;

        try
        {
            foreach (var msg in userMessages)
            {
                await client.Messages.CreateMessageAsync(thread.Id, MessageRole.User, msg);
            }

            var run = (await client.Runs.CreateRunAsync(thread, agent)).Value;

            while (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress)
            {
                await Task.Delay(500);
                run = (await client.Runs.GetRunAsync(thread.Id, run.Id)).Value;
            }

            if (run.Status != RunStatus.Completed)
            {
                _logger.LogWarning("Agent run did not complete successfully. Status: {Status}, LastError: {Error}",
                    run.Status, run.LastError?.Message);
                return null;
            }

            // Newest first; take the most recent assistant text.
            var messages = client.Messages.GetMessages(
                threadId: thread.Id,
                runId: run.Id,
                order: ListSortOrder.Descending);

            foreach (var m in messages)
            {
                if (m.Role != MessageRole.Agent) continue;

                foreach (var part in m.ContentItems)
                {
                    if (part is MessageTextContent text && !string.IsNullOrWhiteSpace(text.Text))
                        return text.Text;
                }
            }

            return null;
        }
        finally
        {
            try { await client.Threads.DeleteThreadAsync(thread.Id); }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to delete thread {ThreadId}", thread.Id); }
        }
    }

    private PersistentAgentsClient GetClient()
    {
        if (_client != null) return _client;

        if (string.IsNullOrWhiteSpace(_settings.ProjectEndpoint))
        {
            throw new InvalidOperationException(
                "Foundry:ProjectEndpoint is not configured. Expected form: " +
                "https://<resource>.services.ai.azure.com/api/projects/<project-name>");
        }

        TokenCredential credential = !string.IsNullOrEmpty(_voiceAgentSettings.ManagedIdentityClientId)
            ? new ManagedIdentityCredential(
                ManagedIdentityId.FromUserAssignedClientId(_voiceAgentSettings.ManagedIdentityClientId!))
            : new DefaultAzureCredential();

        _client = new PersistentAgentsClient(_settings.ProjectEndpoint, credential);
        return _client;
    }

    private async Task<PersistentAgent> GetOrCreateAgentAsync(
        PersistentAgentsClient client,
        CancellationToken cancellationToken = default)
    {
        if (_agent != null) return _agent;

        await _agentLock.WaitAsync(cancellationToken);
        try
        {
            if (_agent != null) return _agent;

            // If a pre-existing agent ID is configured, retrieve it instead of creating a new one.
            if (!string.IsNullOrEmpty(_settings.AgentId))
            {
                var response = await client.Administration.GetAgentAsync(
                    _settings.AgentId, cancellationToken);
                _agent = response.Value;
                _logger.LogInformation(
                    "Using pre-existing Foundry Agent: {AgentId} ({AgentName})",
                    _agent.Id, _agent.Name);
                return _agent;
            }

            // Fall back to creating a new agent when no AgentId is configured.
            var tools = new List<ToolDefinition>();

            if (!string.IsNullOrEmpty(_settings.DataAgentConnectionId))
            {
                var fabricParams = new FabricDataAgentToolParameters();
                fabricParams.ConnectionList.Add(new ToolConnection(_settings.DataAgentConnectionId));
                tools.Add(new MicrosoftFabricToolDefinition(fabricParams));

                _logger.LogInformation(
                    "Configured Foundry Agent with Fabric Data Agent connection: {ConnectionId}",
                    _settings.DataAgentConnectionId);
            }
            else
            {
                _logger.LogWarning("DataAgentConnectionId is not configured. Agent will not have access to data.");
            }

            var response2 = await client.Administration.CreateAgentAsync(
                model: _settings.ModelDeploymentName,
                name: _settings.AgentName,
                instructions: _settings.AgentInstructions,
                tools: tools,
                cancellationToken: cancellationToken);

            _agent = response2.Value;
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
        return _settings.SummaryRequestTemplate
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
        sb.Append("Attention: An alert has been triggered");

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
        if (_disposed) return;
        _agentLock.Dispose();
        _disposed = true;
    }
}
