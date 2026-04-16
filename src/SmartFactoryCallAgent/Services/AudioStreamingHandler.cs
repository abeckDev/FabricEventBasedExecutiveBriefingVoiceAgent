using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Identity;
using Azure.Core;
using Microsoft.Extensions.Options;
using SmartFactoryCallAgent.Configuration;
using SmartFactoryCallAgent.Models;
using SmartFactoryCallAgent.Services;

namespace SmartFactoryCallAgent.Services;

public class AudioStreamingHandler
{
    private readonly OpenAiSettings _openAiSettings;
    private readonly CallContextStore _callContextStore;
    private readonly FoundryAgentService _foundryAgentService;
    private readonly ILogger<AudioStreamingHandler> _logger;

    public AudioStreamingHandler(
        IOptions<OpenAiSettings> openAiSettings,
        CallContextStore callContextStore,
        FoundryAgentService foundryAgentService,
        ILogger<AudioStreamingHandler> logger)
    {
        _openAiSettings = openAiSettings.Value;
        _callContextStore = callContextStore;
        _foundryAgentService = foundryAgentService;
        _logger = logger;
    }

    public async Task HandleAsync(WebSocket acsWebSocket, CancellationToken cancellationToken)
    {
        // First, receive the initial metadata message from ACS to get the callConnectionId
        var buffer = new byte[65536];
        string? callConnectionId = null;

        var result = await acsWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
        if (result.MessageType == WebSocketMessageType.Text)
        {
            var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
            _logger.LogInformation("ACS WebSocket first message: {Message}", message[..Math.Min(message.Length, 500)]);

            var json = JsonNode.Parse(message);
            var kind = json?["kind"]?.GetValue<string>();

            // Try different metadata paths used by ACS
            callConnectionId = json?["audioMetadata"]?["callConnectionId"]?.GetValue<string>()
                ?? json?["metadata"]?["callConnectionId"]?.GetValue<string>()
                ?? json?["callConnectionId"]?.GetValue<string>();

            // If still null, search the CallContextStore for any active context
            if (string.IsNullOrEmpty(callConnectionId))
            {
                callConnectionId = _callContextStore.GetAnyActiveCallConnectionId();
            }

            _logger.LogInformation("ACS WebSocket connected for call {CallConnectionId} (kind={Kind})",
                callConnectionId ?? "unknown", kind ?? "n/a");
        }

        if (string.IsNullOrEmpty(callConnectionId))
        {
            _logger.LogWarning("No callConnectionId found in ACS WebSocket - closing");
            await acsWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "No callConnectionId", cancellationToken);
            return;
        }

        var sanitizedId = Sanitize(callConnectionId);
        var callContext = _callContextStore.Get(callConnectionId);
        if (callContext == null)
        {
            _logger.LogWarning("No call context found for CallConnectionId: {CallConnectionId}", sanitizedId);
            await acsWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "No call context", cancellationToken);
            return;
        }

        var openAiWsUri = BuildOpenAiRealtimeUri();
        using var openAiWebSocket = new ClientWebSocket();

        // Use managed identity for auth (local auth is disabled on the OpenAI resource)
        var credential = new ManagedIdentityCredential("b4d2f012-83a3-4132-9e31-d50f875f57ce");
        var tokenResult = await credential.GetTokenAsync(
            new TokenRequestContext(new[] { "https://cognitiveservices.azure.com/.default" }), cancellationToken);
        openAiWebSocket.Options.SetRequestHeader("Authorization", $"Bearer {tokenResult.Token}");
        _logger.LogInformation("Obtained Azure AD token for OpenAI Realtime API");

        try
        {
            await openAiWebSocket.ConnectAsync(openAiWsUri, cancellationToken);
            _logger.LogInformation("Connected to Azure OpenAI Realtime API for call {CallConnectionId}", sanitizedId);

            // Send session.update with system prompt and tools
            await SendSessionUpdateAsync(openAiWebSocket, callContext, cancellationToken);

            // Trigger OpenAI to immediately speak the executive summary
            var responseCreate = new { type = "response.create" };
            var responseJson = JsonSerializer.Serialize(responseCreate);
            var responseBytes = Encoding.UTF8.GetBytes(responseJson);
            await openAiWebSocket.SendAsync(new ArraySegment<byte>(responseBytes), WebSocketMessageType.Text, true, cancellationToken);
            _logger.LogInformation("Triggered OpenAI Realtime to speak exec summary for call {CallConnectionId}", sanitizedId);

            // Run bidirectional bridging concurrently
            var acsToOpenAi = ForwardAcsToOpenAiAsync(acsWebSocket, openAiWebSocket, cancellationToken);
            var openAiToAcs = ForwardOpenAiToAcsAsync(openAiWebSocket, acsWebSocket, callContext, callConnectionId, cancellationToken);

            await Task.WhenAny(acsToOpenAi, openAiToAcs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in audio streaming for call {CallConnectionId}", sanitizedId);
        }
        finally
        {
            if (openAiWebSocket.State == WebSocketState.Open)
            {
                await openAiWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Session ended", CancellationToken.None);
            }
        }
    }

    private Uri BuildOpenAiRealtimeUri()
    {
        var endpoint = _openAiSettings.Endpoint.TrimEnd('/');
        var wsEndpoint = endpoint.Replace("https://", "wss://").Replace("http://", "ws://");
        return new Uri($"{wsEndpoint}/openai/realtime?api-version=2024-10-01-preview&deployment={_openAiSettings.DeploymentName}");
    }

    private async Task SendSessionUpdateAsync(ClientWebSocket openAiWs, CallContext callContext, CancellationToken ct)
    {
        var systemPrompt = BuildSystemPrompt(callContext);

        var sessionUpdate = new
        {
            type = "session.update",
            session = new
            {
                modalities = new[] { "text", "audio" },
                instructions = systemPrompt,
                voice = "alloy",
                input_audio_format = "pcm16",
                output_audio_format = "pcm16",
                turn_detection = new
                {
                    type = "server_vad",
                    threshold = 0.5,
                    prefix_padding_ms = 300,
                    silence_duration_ms = 500
                },
                tools = new[]
                {
                    new
                    {
                        type = "function",
                        name = "ask_factory_assistant",
                        description = "Ask the factory data assistant a question about production data, machine telemetry, orders, KPIs, or supply chain risks. The assistant has access to the live factory database and will query it to answer the question.",
                        parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                question = new
                                {
                                    type = "string",
                                    description = "A natural language question about factory data, e.g. 'What are the active orders?' or 'Show me the vibration trend for machine M-202 in the last hour'"
                                }
                            },
                            required = new[] { "question" }
                        }
                    }
                },
                tool_choice = "auto"
            }
        };

        var json = JsonSerializer.Serialize(sessionUpdate);
        var bytes = Encoding.UTF8.GetBytes(json);
        await openAiWs.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    private static string BuildSystemPrompt(CallContext callContext)
    {
        return $"""
            You are a smart factory AI assistant calling a factory manager with an urgent alert.
            
            IMPORTANT: Start the conversation IMMEDIATELY by reading this executive summary aloud:
            "{callContext.ExecSummary}"
            
            After reading the summary, say: "I'm here to answer any follow-up questions about this alert. What would you like to know?"
            
            You can answer follow-up questions from the manager about the machine anomaly, production impact,
            and recommended actions. Use the ask_factory_assistant tool to fetch fresh data from the factory
            database when needed.
            
            Be concise and professional. When the manager says "goodbye", "hang up", or "that's all", 
            end the conversation politely.
            """;
    }

    private async Task ForwardAcsToOpenAiAsync(
        WebSocket acsWs,
        ClientWebSocket openAiWs,
        CancellationToken ct)
    {
        var buffer = new byte[65536];
        try
        {
            while (acsWs.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await acsWs.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("ACS WebSocket closed");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    var json = JsonNode.Parse(message);
                    var kind = json?["kind"]?.GetValue<string>();

                    if (kind == "AudioData")
                    {
                        var audioData = json?["audioData"]?["data"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(audioData))
                        {
                            var audioAppend = new
                            {
                                type = "input_audio_buffer.append",
                                audio = audioData
                            };
                            var appendJson = JsonSerializer.Serialize(audioAppend);
                            var appendBytes = Encoding.UTF8.GetBytes(appendJson);
                            if (openAiWs.State == WebSocketState.Open)
                            {
                                await openAiWs.SendAsync(new ArraySegment<byte>(appendBytes), WebSocketMessageType.Text, true, ct);
                            }
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error forwarding ACS audio to OpenAI");
        }
    }

    private async Task ForwardOpenAiToAcsAsync(
        ClientWebSocket openAiWs,
        WebSocket acsWs,
        CallContext callContext,
        string callConnectionId,
        CancellationToken ct)
    {
        var buffer = new byte[65536];
        var messageBuilder = new StringBuilder();

        try
        {
            while (openAiWs.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await openAiWs.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("OpenAI WebSocket closed");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                    if (result.EndOfMessage)
                    {
                        var message = messageBuilder.ToString();
                        messageBuilder.Clear();

                        await ProcessOpenAiMessageAsync(message, openAiWs, acsWs, callConnectionId, ct);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error forwarding OpenAI audio to ACS");
        }
    }

    private async Task ProcessOpenAiMessageAsync(
        string message,
        ClientWebSocket openAiWs,
        WebSocket acsWs,
        string callConnectionId,
        CancellationToken ct)
    {
        try
        {
            var json = JsonNode.Parse(message);
            var eventType = json?["type"]?.GetValue<string>();

            switch (eventType)
            {
                case "response.audio.delta":
                    var audioDelta = json?["delta"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(audioDelta) && acsWs.State == WebSocketState.Open)
                    {
                        var acsAudioMessage = new
                        {
                            kind = "AudioData",
                            audioData = new { data = audioDelta }
                        };
                        var acsJson = JsonSerializer.Serialize(acsAudioMessage);
                        var acsBytes = Encoding.UTF8.GetBytes(acsJson);
                        await acsWs.SendAsync(new ArraySegment<byte>(acsBytes), WebSocketMessageType.Text, true, ct);
                    }
                    break;

                case "response.function_call_arguments.done":
                    await HandleFunctionCallAsync(json!, openAiWs, callConnectionId, ct);
                    break;

                case "response.output_item.done":
                    // Check for transcript "goodbye" or "hang up"
                    var transcript = json?["item"]?["content"]?[0]?["transcript"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(transcript) &&
                        (transcript.Contains("goodbye", StringComparison.OrdinalIgnoreCase) ||
                         transcript.Contains("hang up", StringComparison.OrdinalIgnoreCase) ||
                         transcript.Contains("that's all", StringComparison.OrdinalIgnoreCase)))
                    {
                        _logger.LogInformation("Manager requested hang up for call {CallConnectionId}", Sanitize(callConnectionId));
                        _callContextStore.Remove(callConnectionId);
                    }
                    break;

                case "error":
                    _logger.LogError("OpenAI Realtime API error: {Message}", message);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing OpenAI message: {Message}", message[..Math.Min(message.Length, 200)]);
        }
    }

    private async Task HandleFunctionCallAsync(JsonNode json, ClientWebSocket openAiWs, string callConnectionId, CancellationToken ct)
    {
        var callId = json["call_id"]?.GetValue<string>();
        var name = json["name"]?.GetValue<string>();
        var argumentsStr = json["arguments"]?.GetValue<string>();

        if (name != "ask_factory_assistant" || string.IsNullOrEmpty(argumentsStr))
            return;

        string queryResult;
        try
        {
            var args = JsonNode.Parse(argumentsStr);
            var question = args?["question"]?.GetValue<string>() ?? string.Empty;
            _logger.LogInformation("Forwarding question to factory assistant: {Question}", question[..Math.Min(question.Length, 100)]);

            var callContext = _callContextStore.Get(callConnectionId);
            var execSummaryContext = callContext?.ExecSummary ?? string.Empty;
            queryResult = await _foundryAgentService.AnswerFollowUpAsync(question, execSummaryContext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling factory assistant from function call");
            queryResult = "I'm sorry, I was unable to retrieve that information right now.";
        }

        // Send function output back to OpenAI
        var functionOutput = new
        {
            type = "conversation.item.create",
            item = new
            {
                type = "function_call_output",
                call_id = callId,
                output = queryResult
            }
        };
        var outputJson = JsonSerializer.Serialize(functionOutput);
        var outputBytes = Encoding.UTF8.GetBytes(outputJson);
        await openAiWs.SendAsync(new ArraySegment<byte>(outputBytes), WebSocketMessageType.Text, true, ct);

        // Trigger response generation
        var responseCreate = new { type = "response.create" };
        var responseJson = JsonSerializer.Serialize(responseCreate);
        var responseBytes = Encoding.UTF8.GetBytes(responseJson);
        await openAiWs.SendAsync(new ArraySegment<byte>(responseBytes), WebSocketMessageType.Text, true, ct);
    }

    private static string Sanitize(string value)
        => value.ReplaceLineEndings(" ").Replace("\t", " ");
}
