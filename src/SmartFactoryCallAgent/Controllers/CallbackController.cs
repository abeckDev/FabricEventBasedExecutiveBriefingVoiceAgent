using Azure.Communication.CallAutomation;
using Azure.Messaging;
using Microsoft.AspNetCore.Mvc;
using SmartFactoryCallAgent.Models;

namespace SmartFactoryCallAgent.Controllers;

[ApiController]
[Route("api/callbacks")]
public class CallbackController : ControllerBase
{
    private readonly CallContextStore _callContextStore;
    private readonly ILogger<CallbackController> _logger;

    private const string VoiceName = "en-US-AndrewMultilingualNeural";
    private const string FollowUpPrompt = "You may now ask me any follow-up questions about the machine anomaly or production impact.";

    public CallbackController(
        CallContextStore callContextStore,
        ILogger<CallbackController> logger)
    {
        _callContextStore = callContextStore;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> HandleCallback([FromBody] CloudEvent[] cloudEvents)
    {
        foreach (var cloudEvent in cloudEvents)
        {
            CallAutomationEventBase? acsEvent;
            try
            {
                acsEvent = CallAutomationEventParser.Parse(cloudEvent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse ACS cloud event");
                continue;
            }

            _logger.LogInformation("ACS Event received: {EventType} for call {CallConnectionId}",
                acsEvent?.GetType().Name, acsEvent?.CallConnectionId);

            switch (acsEvent)
            {
                case CallConnected callConnected:
                    await HandleCallConnectedAsync(callConnected);
                    break;

                case PlayCompleted playCompleted:
                    await HandlePlayCompletedAsync(playCompleted);
                    break;

                case PlayFailed playFailed:
                    HandlePlayFailed(playFailed);
                    break;

                case CallDisconnected callDisconnected:
                    HandleCallDisconnected(callDisconnected);
                    break;
            }
        }

        return Ok();
    }

    private async Task HandleCallConnectedAsync(CallConnected callConnected)
    {
        _logger.LogInformation("Call connected: {CallConnectionId}", callConnected.CallConnectionId);

        var callContext = _callContextStore.Get(callConnected.CallConnectionId);
        if (callContext == null)
        {
            _logger.LogWarning("No context found for CallConnectionId: {CallConnectionId}", callConnected.CallConnectionId);
            return;
        }

        try
        {
            var client = GetCallConnectionClient(callConnected.CallConnectionId);
            var callMedia = client.GetCallMedia();

            var textSource = new TextSource(callContext.ExecSummary)
            {
                VoiceName = VoiceName
            };

            await callMedia.PlayToAllAsync(textSource);
            _logger.LogInformation("Playing exec summary to call {CallConnectionId}", callConnected.CallConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error playing exec summary for call {CallConnectionId}", callConnected.CallConnectionId);
        }
    }

    private async Task HandlePlayCompletedAsync(PlayCompleted playCompleted)
    {
        _logger.LogInformation("Play completed for call: {CallConnectionId}", playCompleted.CallConnectionId);

        try
        {
            var client = GetCallConnectionClient(playCompleted.CallConnectionId);
            var callMedia = client.GetCallMedia();

            // Play the follow-up prompt
            var promptSource = new TextSource(FollowUpPrompt)
            {
                VoiceName = VoiceName
            };

            await callMedia.PlayToAllAsync(promptSource);

            // Start bidirectional media streaming
            var startStreamingOptions = new StartMediaStreamingOptions
            {
                OperationContext = "bidirectional-audio"
            };
            await callMedia.StartMediaStreamingAsync(startStreamingOptions);

            _logger.LogInformation("Started bidirectional media streaming for call {CallConnectionId}", playCompleted.CallConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting media streaming for call {CallConnectionId}", playCompleted.CallConnectionId);
        }
    }

    private void HandlePlayFailed(PlayFailed playFailed)
    {
        _logger.LogError("Play failed for call {CallConnectionId}: {ResultInfo}",
            playFailed.CallConnectionId, playFailed.ResultInformation?.Message);
    }

    private void HandleCallDisconnected(CallDisconnected callDisconnected)
    {
        _logger.LogInformation("Call disconnected: {CallConnectionId}", callDisconnected.CallConnectionId);
        _callContextStore.Remove(callDisconnected.CallConnectionId);
    }

    private CallConnection GetCallConnectionClient(string callConnectionId)
    {
        var connectionString = HttpContext.RequestServices
            .GetRequiredService<IConfiguration>()["Acs:ConnectionString"]
            ?? throw new InvalidOperationException("ACS ConnectionString not configured");

        var automationClient = new CallAutomationClient(connectionString);
        return automationClient.GetCallConnection(callConnectionId);
    }
}
