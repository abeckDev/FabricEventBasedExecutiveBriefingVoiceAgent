using Microsoft.AspNetCore.Mvc;
using SmartFactoryCallAgent.Models;
using SmartFactoryCallAgent.Services;

namespace SmartFactoryCallAgent.Controllers;

[ApiController]
[Route("api/alert")]
public class AlertController : ControllerBase
{
    private readonly FoundryAgentService _foundryAgentService;
    private readonly CallService _callService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AlertController> _logger;

    public AlertController(
        FoundryAgentService foundryAgentService,
        CallService callService,
        IConfiguration configuration,
        ILogger<AlertController> logger)
    {
        _foundryAgentService = foundryAgentService;
        _callService = callService;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> ReceiveAlert([FromBody] DataActivatorAlert alert)
    {
        _logger.LogInformation(
            "Received Data Activator alert: Machine={MachineId}, Station={StationName}, Vibration={Vibration}g",
            Sanitize(alert.MachineId), Sanitize(alert.StationName), alert.Vibration);

        try
        {
            // 1. Generate executive summary via Foundry Agent (queries Data Agent internally)
            var execSummary = await _foundryAgentService.GenerateExecSummaryAsync(alert);
            _logger.LogInformation("Generated exec summary ({Length} chars)", execSummary.Length);

            // 2. Place outbound PSTN call
            var managerPhone = _configuration["ManagerPhoneNumber"]
                ?? throw new InvalidOperationException("ManagerPhoneNumber not configured");

            var callConnectionId = await _callService.PlaceCallWithSummaryAsync(managerPhone, execSummary);

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
                machineId = alert.MachineId,
                stationName = alert.StationName
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing alert for machine {MachineId}", Sanitize(alert.MachineId));
            return StatusCode(500, new { status = "error", message = ex.Message });
        }
    }

    private static string Sanitize(string? value)
        => value?.ReplaceLineEndings(" ").Replace("\t", " ") ?? string.Empty;
}
