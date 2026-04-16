using Azure.Communication;
using Azure.Communication.CallAutomation;
using Microsoft.Extensions.Options;
using FabricVoiceCallAgent.Configuration;
using FabricVoiceCallAgent.Models;

namespace FabricVoiceCallAgent.Services;

public class CallService
{
    private readonly AcsSettings _acsSettings;
    private readonly CallContextStore _callContextStore;
    private readonly ILogger<CallService> _logger;

    public CallService(
        IOptions<AcsSettings> acsSettings,
        CallContextStore callContextStore,
        ILogger<CallService> logger)
    {
        _acsSettings = acsSettings.Value;
        _callContextStore = callContextStore;
        _logger = logger;
    }

    public async Task<string?> PlaceCallWithSummaryAsync(
        string phoneNumber,
        string execSummary,
        AlertPayload? alert = null)
    {
        try
        {
            var client = new CallAutomationClient(_acsSettings.ConnectionString);

            var callbackUri = new Uri($"{_acsSettings.CallbackBaseUrl}/api/callbacks");
            var wsTransportUri = new Uri($"{_acsSettings.CallbackBaseUrl.Replace("https://", "wss://").Replace("http://", "ws://")}/ws/audio");

            var target = new PhoneNumberIdentifier(phoneNumber);
            var caller = new PhoneNumberIdentifier(_acsSettings.PhoneNumber);
            var callInvite = new CallInvite(target, caller);

            var mediaStreamingOptions = new MediaStreamingOptions(
                MediaStreamingAudioChannel.Mixed,
                StreamingTransport.Websocket)
            {
                TransportUri = wsTransportUri,
                MediaStreamingContent = MediaStreamingContent.Audio,
                EnableBidirectional = true,
                AudioFormat = AudioFormat.Pcm24KMono
            };

            var createCallOptions = new CreateCallOptions(callInvite, callbackUri)
            {
                MediaStreamingOptions = mediaStreamingOptions
            };

            var response = await client.CreateCallAsync(createCallOptions);
            var callConnectionId = response.Value.CallConnectionProperties.CallConnectionId;

            _logger.LogInformation("Outbound call placed. CallConnectionId: {CallConnectionId}", callConnectionId);

            _callContextStore.Set(callConnectionId, new CallContext
            {
                CallConnectionId = callConnectionId,
                ExecSummary = execSummary,
                PhoneNumber = phoneNumber,
                Alert = alert
            });

            return callConnectionId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error placing outbound call to phone number (masked)");
            return null;
        }
    }
}
