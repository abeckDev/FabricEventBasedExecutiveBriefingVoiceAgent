using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using FabricVoiceCallAgent.Configuration;
using FabricVoiceCallAgent.Models;

namespace FabricVoiceCallAgent.Services;

/// <summary>
/// Typed HTTP client that calls the FabricDataService backend's /ask endpoint.
/// Returns the full response to let callers decide fallback behavior.
/// </summary>
public class FabricBackendClient
{
    private readonly HttpClient _httpClient;
    private readonly FabricBackendSettings _settings;
    private readonly ILogger<FabricBackendClient> _logger;

    public FabricBackendClient(
        HttpClient httpClient,
        IOptions<FabricBackendSettings> settings,
        ILogger<FabricBackendClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Sends a question to the FabricDataService backend.
    /// Returns a typed result with answer, metadata, and success/failure status.
    /// Callers are responsible for their own fallback behavior on failure.
    /// </summary>
    public async Task<FabricBackendResult> AskAsync(
        string question,
        AlertPayload? alert = null,
        string? priorSummary = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..12];
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(_settings.DefaultTimeoutSeconds);

        _logger.LogInformation(
            "Calling FabricDataService [correlation={CorrelationId}, timeout={Timeout}s]: {Question}",
            correlationId, effectiveTimeout.TotalSeconds, question[..Math.Min(question.Length, 100)]);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(effectiveTimeout);

            var request = new
            {
                question,
                alertContext = BuildAlertContext(alert),
                priorSummary,
                idempotencyKey = correlationId
            };

            var response = await _httpClient.PostAsJsonAsync("/ask", request, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cts.Token);
                _logger.LogWarning(
                    "FabricDataService returned {StatusCode} [correlation={CorrelationId}]: {Body}",
                    (int)response.StatusCode, correlationId, errorBody[..Math.Min(errorBody.Length, 500)]);

                return FabricBackendResult.Failure(
                    $"Backend returned HTTP {(int)response.StatusCode}",
                    correlationId);
            }

            var askResponse = await response.Content.ReadFromJsonAsync<FabricAskResponse>(cts.Token);

            if (askResponse == null || string.IsNullOrWhiteSpace(askResponse.Answer))
            {
                _logger.LogWarning("FabricDataService returned empty answer [correlation={CorrelationId}]", correlationId);
                return FabricBackendResult.Failure("Backend returned empty answer", correlationId);
            }

            _logger.LogInformation(
                "FabricDataService responded [correlation={CorrelationId}, backend={Backend}, duration={Duration}ms, answer={AnswerLen} chars]",
                correlationId,
                askResponse.Metadata?.BackendUsed ?? "unknown",
                askResponse.Metadata?.DurationMs ?? 0,
                askResponse.Answer.Length);

            return FabricBackendResult.Success(
                askResponse.Answer,
                correlationId,
                askResponse.Metadata?.BackendUsed,
                askResponse.Metadata?.DurationMs ?? 0);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("FabricDataService call cancelled [correlation={CorrelationId}]", correlationId);
            throw; // Caller-initiated cancellation — propagate
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("FabricDataService call timed out after {Timeout}s [correlation={CorrelationId}]",
                effectiveTimeout.TotalSeconds, correlationId);
            return FabricBackendResult.Failure(
                $"Backend timed out after {effectiveTimeout.TotalSeconds}s",
                correlationId);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "FabricDataService connection error [correlation={CorrelationId}]", correlationId);
            return FabricBackendResult.Failure($"Connection error: {ex.Message}", correlationId);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "FabricDataService returned invalid JSON [correlation={CorrelationId}]", correlationId);
            return FabricBackendResult.Failure("Invalid response from backend", correlationId);
        }
    }

    private static Dictionary<string, object>? BuildAlertContext(AlertPayload? alert)
    {
        if (alert == null) return null;

        var ctx = new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(alert.SourceId)) ctx["sourceId"] = alert.SourceId;
        if (!string.IsNullOrEmpty(alert.SourceName)) ctx["sourceName"] = alert.SourceName;
        if (!string.IsNullOrEmpty(alert.AlertType)) ctx["alertType"] = alert.AlertType;
        if (!string.IsNullOrEmpty(alert.Severity)) ctx["severity"] = alert.Severity;
        if (!string.IsNullOrEmpty(alert.Title)) ctx["title"] = alert.Title;
        if (!string.IsNullOrEmpty(alert.Description)) ctx["description"] = alert.Description;
        if (alert.Timestamp.HasValue) ctx["timestamp"] = alert.Timestamp.Value.ToString("u");
        if (alert.Metadata != null)
        {
            foreach (var kv in alert.Metadata)
                ctx[kv.Key] = kv.Value;
        }

        return ctx.Count > 0 ? ctx : null;
    }
}

/// <summary>
/// Result from the FabricDataService backend, preserving success/failure status and metadata.
/// </summary>
public class FabricBackendResult
{
    public bool IsSuccess { get; init; }
    public string? Answer { get; init; }
    public string? ErrorMessage { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public string? BackendUsed { get; init; }
    public long DurationMs { get; init; }

    public static FabricBackendResult Success(string answer, string correlationId, string? backendUsed, long durationMs) =>
        new() { IsSuccess = true, Answer = answer, CorrelationId = correlationId, BackendUsed = backendUsed, DurationMs = durationMs };

    public static FabricBackendResult Failure(string error, string correlationId) =>
        new() { IsSuccess = false, ErrorMessage = error, CorrelationId = correlationId };
}

/// <summary>
/// DTO matching the FabricDataService /ask response shape.
/// </summary>
internal class FabricAskResponse
{
    public string Answer { get; set; } = string.Empty;
    public FabricAskResponseMetadata? Metadata { get; set; }
}

internal class FabricAskResponseMetadata
{
    public string? BackendUsed { get; set; }
    public long DurationMs { get; set; }
    public string? CorrelationId { get; set; }
    public int ToolCallsCount { get; set; }
}
