namespace FabricVoiceCallAgent.Configuration;

public class AcsSettings
{
    public string ConnectionString { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string CallbackBaseUrl { get; set; } = string.Empty;
}

public class OpenAiSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string DeploymentName { get; set; } = "gpt-4o-realtime";
    public string Voice { get; set; } = "alloy";
}

public class FabricBackendSettings
{
    /// <summary>
    /// Base URL of the FabricDataService (e.g., http://fabricdataservice:8080)
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Default timeout in seconds for backend calls
    /// </summary>
    public int DefaultTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Timeout in seconds for follow-up questions during live calls (shorter for better UX)
    /// </summary>
    public int FollowUpTimeoutSeconds { get; set; } = 20;
}

public class VoiceAgentSettings
{
    /// <summary>
    /// Default phone number to call when alert doesn't specify one
    /// </summary>
    public string DefaultPhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// Managed identity client ID for Azure authentication (optional)
    /// </summary>
    public string? ManagedIdentityClientId { get; set; }

    /// <summary>
    /// System prompt template for the OpenAI Realtime voice agent.
    /// Use {ExecSummary} placeholder for the executive summary.
    /// </summary>
    public string SystemPromptTemplate { get; set; } = """
        You are an AI assistant calling a user with an urgent alert notification.
        
        IMPORTANT: Start the conversation IMMEDIATELY by reading this executive summary aloud:
        "{ExecSummary}"
        
        After reading the summary, say: "I'm here to answer any follow-up questions about this alert. What would you like to know?"
        
        You can answer follow-up questions from the user about the alert and related data.
        Use the ask_data_assistant tool to fetch fresh data from the database when needed.
        
        Be concise and professional. When the user says "goodbye", "hang up", or "that's all", 
        end the conversation politely.
        """;

    /// <summary>
    /// Description for the data assistant tool that OpenAI will use for follow-up queries
    /// </summary>
    public string DataAssistantToolDescription { get; set; } = 
        "Ask the data assistant a question about the data. The assistant has access to the live database and will query it to answer the question.";

    /// <summary>
    /// Template for building the executive summary request sent to the backend.
    /// Supports placeholders: {AlertType}, {SourceId}, {SourceName}, {Severity},
    /// {Title}, {Description}, {Timestamp}, {Metadata}
    /// </summary>
    public string SummaryRequestTemplate { get; set; } = """
        A webhook alert has been triggered. Please query live data and generate a concise ~30-second spoken executive summary.

        Follow these steps:
        1. Check the current overall status, any ongoing disruptions, anomalies, or problems.
        2. Look for information specifically related to the alert details below.
        3. Generate the summary:
           - If matching data is found: summarize the issue, its impact based on real data, and recommended actions.
           - If no matching data is found: report that a notification was received but no corresponding issue was confirmed, then briefly summarize the current overall status.

        Alert details:
          Type: {AlertType}
          Source ID: {SourceId}
          Source Name: {SourceName}
          Severity: {Severity}
          Title: {Title}
          Description: {Description}
          Timestamp: {Timestamp}
          Additional Data: {Metadata}
        """;
}
