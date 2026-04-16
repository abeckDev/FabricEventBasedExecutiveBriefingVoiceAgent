using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using FabricVoiceCallAgent.Configuration;
using FabricVoiceCallAgent.Models;
using FabricVoiceCallAgent.Services;

namespace FabricVoiceCallAgent.Controllers;

[ApiController]
[Route("api/alert")]
public class AlertController : ControllerBase
{
    private readonly FoundryAgentService _foundryAgentService;
    private readonly CallService _callService;
    private readonly VoiceAgentSettings _voiceAgentSettings;
    private readonly ILogger<AlertController> _logger;

    public AlertController(
        FoundryAgentService foundryAgentService,
        CallService callService,
        IOptions<VoiceAgentSettings> voiceAgentSettings,
        ILogger<AlertController> logger)
    {
        _foundryAgentService = foundryAgentService;
        _callService = callService;
        _voiceAgentSettings = voiceAgentSettings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Receives an alert payload, generates an executive summary using the Foundry Agent,
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
            // 1. Generate executive summary via Foundry Agent (queries Data Agent internally)
            var execSummary = await _foundryAgentService.GenerateExecSummaryAsync(alert);
            _logger.LogInformation("Generated exec summary ({Length} chars)", execSummary.Length);

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
                sourceName = alert.SourceName
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing alert for source {SourceId}", Sanitize(alert.SourceId));
            return StatusCode(500, new { status = "error", message = ex.Message });
        }
    }

    private static string Sanitize(string? value)
        => value?.ReplaceLineEndings(" ").Replace("\t", " ") ?? string.Empty;
}
