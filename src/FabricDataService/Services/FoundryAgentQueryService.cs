#pragma warning disable OPENAI001 // Experimental API

using System.Diagnostics;
using System.Text;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using OpenAI.Responses;
using FabricDataService.Configuration;
using FabricDataService.Models;

namespace FabricDataService.Services;

/// <summary>
/// Queries Fabric data via an Azure AI Foundry Agent that has a Microsoft Fabric
/// Data Agent tool attached. Uses the GA Azure.AI.Projects 2.0.0 SDK with
/// the ProjectResponsesClient (Responses API).
///
/// The agent is referenced by name (new Foundry Agent naming model).
/// A pre-provisioned agent with the correct Fabric Data Agent tool is required.
/// </summary>
public class FoundryAgentQueryService : IDataQueryService, IDisposable
{
    private readonly FabricDataServiceSettings _settings;
    private readonly ILogger<FoundryAgentQueryService> _logger;

    private AIProjectClient? _projectClient;
    private ProjectResponsesClient? _responsesClient;
    private ProjectsAgentRecord? _agentRecord;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _disposed;

    public FoundryAgentQueryService(
        IOptions<FabricDataServiceSettings> settings,
        ILogger<FoundryAgentQueryService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<DataQueryResponse> QueryAsync(
        DataQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var responsesClient = await GetResponsesClientAsync(cancellationToken);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_settings.RunTimeoutSeconds));

        try
        {
            // Build the full prompt including context and question
            var fullPrompt = BuildPrompt(request);

            // Use the Responses API — synchronous agent invocation
            var result = await responsesClient.CreateResponseAsync(
                fullPrompt,
                previousResponseId: null,
                cancellationToken: timeoutCts.Token);

            var response = result.Value;

            // Count tool calls and extract text output
            int toolCallsCount = 0;
            var answerBuilder = new StringBuilder();

            foreach (var item in response.OutputItems)
            {
                switch (item)
                {
                    case MessageResponseItem message:
                        foreach (var content in message.Content)
                        {
                            if (!string.IsNullOrEmpty(content.Text))
                            {
                                answerBuilder.Append(content.Text);
                            }
                        }
                        break;

                    case FunctionCallResponseItem functionCall:
                        toolCallsCount++;
                        _logger.LogInformation(
                            "Function call: Name={FunctionName}, CallId={CallId}, CorrelationId={CorrelationId}",
                            functionCall.FunctionName, functionCall.CallId, request.CorrelationId);
                        break;

                    default:
                        LogResponseItem(item, request.CorrelationId);
                        break;
                }
            }

            // Log usage if available
            if (response.Usage != null)
            {
                _logger.LogInformation(
                    "Token usage: Input={InputTokens}, Output={OutputTokens}, CorrelationId={CorrelationId}",
                    response.Usage.InputTokenCount, response.Usage.OutputTokenCount, request.CorrelationId);
            }

            sw.Stop();
            var answer = answerBuilder.Length > 0
                ? answerBuilder.ToString()
                : "The agent completed but produced no text response.";

            _logger.LogInformation(
                "Query completed. DurationMs={DurationMs}, ToolCalls={ToolCalls}, Status={Status}, CorrelationId={CorrelationId}",
                sw.ElapsedMilliseconds, toolCallsCount, response.Status, request.CorrelationId);

            return new DataQueryResponse
            {
                Answer = answer,
                BackendUsed = "foundry-agent",
                DurationMs = sw.ElapsedMilliseconds,
                CorrelationId = request.CorrelationId,
                ToolCallsCount = toolCallsCount
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogWarning(
                "Query cancelled by caller after {ElapsedMs}ms. CorrelationId={CorrelationId}",
                sw.ElapsedMilliseconds, request.CorrelationId);
            throw; // Propagate caller-initiated cancellation
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _logger.LogWarning(
                "Query timed out after {TimeoutSeconds}s ({ElapsedMs}ms elapsed). CorrelationId={CorrelationId}",
                _settings.RunTimeoutSeconds, sw.ElapsedMilliseconds, request.CorrelationId);

            return new DataQueryResponse
            {
                Answer = $"The query timed out after {_settings.RunTimeoutSeconds} seconds.",
                BackendUsed = "foundry-agent",
                DurationMs = sw.ElapsedMilliseconds,
                CorrelationId = request.CorrelationId,
                ToolCallsCount = 0
            };
        }
    }

    public async Task<string?> CheckReadinessAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_settings.ProjectEndpoint))
                return "ProjectEndpoint is not configured";

            if (string.IsNullOrWhiteSpace(_settings.AgentId))
                return "AgentId is not configured";

            var projectClient = GetProjectClient();
            var agent = await GetAgentRecordAsync(projectClient, cancellationToken);

            _logger.LogInformation(
                "Readiness check passed. Agent={AgentName} (Id={AgentId})",
                agent.Name, agent.Id);

            return null; // healthy
        }
        catch (Exception ex)
        {
            return $"Readiness check failed: {ex.Message}";
        }
    }

    private AIProjectClient GetProjectClient()
    {
        if (_projectClient != null) return _projectClient;

        TokenCredential credential = !string.IsNullOrEmpty(_settings.ManagedIdentityClientId)
            ? new ManagedIdentityCredential(
                ManagedIdentityId.FromUserAssignedClientId(_settings.ManagedIdentityClientId!))
            : new DefaultAzureCredential();

        _projectClient = new AIProjectClient(
            new Uri(_settings.ProjectEndpoint), credential);

        return _projectClient;
    }

    private async Task<ProjectsAgentRecord> GetAgentRecordAsync(
        AIProjectClient projectClient,
        CancellationToken cancellationToken)
    {
        if (_agentRecord != null) return _agentRecord;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_agentRecord != null) return _agentRecord;

            var response = await projectClient.AgentAdministrationClient
                .GetAgentAsync(_settings.AgentId, cancellationToken);

            _agentRecord = response.Value;

            _logger.LogInformation(
                "Retrieved Foundry Agent: Name={AgentName}, Id={AgentId}",
                _agentRecord.Name, _agentRecord.Id);

            return _agentRecord;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<ProjectResponsesClient> GetResponsesClientAsync(
        CancellationToken cancellationToken)
    {
        if (_responsesClient != null) return _responsesClient;

        var projectClient = GetProjectClient();
        var agent = await GetAgentRecordAsync(projectClient, cancellationToken);

        // Create a ProjectResponsesClient scoped to the agent
        var agentRef = new AgentReference(agent.Name, version: null);
        _responsesClient = projectClient.ProjectOpenAIClient
            .GetProjectResponsesClientForAgent(defaultAgent: agentRef);

        _logger.LogInformation("Created ProjectResponsesClient for agent {AgentName}", agent.Name);

        return _responsesClient;
    }

    private static string BuildPrompt(DataQueryRequest request)
    {
        var sb = new StringBuilder();

        foreach (var contextMsg in request.ContextMessages)
        {
            sb.AppendLine(contextMsg);
            sb.AppendLine();
        }

        sb.Append(request.Question);
        return sb.ToString();
    }

    private void LogResponseItem(ResponseItem item, string correlationId)
    {
        if (!_settings.EnableDebugToolLogging)
        {
            _logger.LogInformation(
                "Response item: Type={ItemType}, Id={ItemId}, CorrelationId={CorrelationId}",
                item.GetType().Name, item.Id, correlationId);
            return;
        }

        // Detailed debug logging with property enumeration
        var sb = new StringBuilder();
        foreach (var prop in item.GetType().GetProperties(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            object? val = null;
            try { val = prop.GetValue(item); }
            catch { continue; }

            var valStr = val?.ToString() ?? "null";
            if (valStr.Length > 500) valStr = valStr[..500] + "…";
            sb.Append(prop.Name).Append('=').Append(valStr).Append("; ");
        }

        _logger.LogInformation(
            "Response item detail: Type={ItemType}, Props={Props}, CorrelationId={CorrelationId}",
            item.GetType().Name, sb.ToString(), correlationId);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _initLock.Dispose();
        _disposed = true;
    }
}
