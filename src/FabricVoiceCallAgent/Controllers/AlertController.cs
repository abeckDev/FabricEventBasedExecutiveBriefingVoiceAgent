using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using FabricVoiceCallAgent.Configuration;
using FabricVoiceCallAgent.Models;
using FabricVoiceCallAgent.Services;
using System.Text;

namespace FabricVoiceCallAgent.Controllers;

[ApiController]
[Route("api/alert")]
public class AlertController : ControllerBase
{
    private readonly FabricBackendClient _fabricClient;
    private readonly CallService _callService;
    private readonly VoiceAgentSettings _voiceAgentSettings;
    private readonly ILogger<AlertController> _logger;

    public AlertController(
        FabricBackendClient fabricClient,
        CallService callService,
        IOptions<VoiceAgentSettings> voiceAgentSettings,
        ILogger<AlertController> logger)
    {
        _fabricClient = fabricClient;
        _callService = callService;
        _voiceAgentSettings = voiceAgentSettings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Receives an alert payload, generates an executive summary via the Fabric backend,
    /// and places an outbound voice call to deliver the summary.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ReceiveAlert([FromBody] AlertPayload alert)
    {
        _logger.LogInformation(
            "Received alert: Source={SourceId}, Name={SourceName}, Type={AlertType}, Severity={Severity}",
            Sanitize(alert.SourceId), Sanitize(alert.SourceName), Sanitize(alert.AlertType), Sanitize(alert.Severity));

        try
        {
            // 1. Generate executive summary via Fabric backend
            var question = BuildSummaryRequest(alert);
            var result = await _fabricClient.AskAsync(
                question, alert, cancellationToken: HttpContext.RequestAborted);

            var execSummary = result.IsSuccess
                ? result.Answer!
                : BuildFallbackSummary(alert);

            if (!result.IsSuccess)
            {
                _logger.LogWarning(
                    "Fabric backend failed [correlation={CorrelationId}]: {Error}. Using fallback summary.",
                    result.CorrelationId, result.ErrorMessage);
            }

            _logger.LogInformation("Executive summary ready ({Length} chars, backend={Backend})",
                execSummary.Length, result.BackendUsed ?? "fallback");

            // 2. Determine phone number (alert override > default config)
            var phoneNumber = !string.IsNullOrEmpty(alert.PhoneNumber) 
                ? alert.PhoneNumber 
                : _voiceAgentSettings.DefaultPhoneNumber;

            if (string.IsNullOrEmpty(phoneNumber))
            {
                _logger.LogError("No phone number configured in alert or default settings");
                return BadRequest(new { status = "error", message = "No phone number configured" });
            }

            // 3. Place outbound PSTN call
            var callConnectionId = await _callService.PlaceCallWithSummaryAsync(phoneNumber, execSummary, alert);

            if (callConnectionId == null)
            {
                _logger.LogError("Failed to place outbound call");
                return StatusCode(500, new { status = "error", message = "Failed to place outbound call" });
            }

            return Ok(new
            {
                status = "success",
                callConnectionId,
                execSummary,
                sourceId = alert.SourceId,
                sourceName = alert.SourceName,
                correlationId = result.CorrelationId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing alert for source {SourceId}", Sanitize(alert.SourceId));
            return StatusCode(500, new { status = "error", message = ex.Message });
        }
    }

    private string BuildSummaryRequest(AlertPayload alert)
    {
        return _voiceAgentSettings.SummaryRequestTemplate
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

    private static string Sanitize(string? value)
        => value?.ReplaceLineEndings(" ").Replace("\t", " ") ?? string.Empty;
}
