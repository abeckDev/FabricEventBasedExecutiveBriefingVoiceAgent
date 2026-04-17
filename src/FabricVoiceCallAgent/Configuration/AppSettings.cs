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

public class FoundrySettings
{
    /// <summary>
    /// Azure AI Foundry project endpoint (account-based project), in the form
    /// <c>https://&lt;resource&gt;.services.ai.azure.com/api/projects/&lt;project-name&gt;</c>.
    /// </summary>
    public string ProjectEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Model deployment name for the Foundry Agent (e.g., gpt-4o)
    /// </summary>
    public string ModelDeploymentName { get; set; } = "gpt-4o";

    /// <summary>
    /// ID of a pre-existing Foundry Agent to reuse. When set, the service
    /// retrieves this agent instead of creating a new one each session.
    /// </summary>
    public string AgentId { get; set; } = string.Empty;

    /// <summary>
    /// Connection ID of the Fabric Data Agent in the AI Foundry project.
    /// Only used when AgentId is empty (i.e. creating a new agent).
    /// </summary>
    public string DataAgentConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// API key for Foundry (optional, uses managed identity if not provided)
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Name for the Foundry Agent
    /// </summary>
    public string AgentName { get; set; } = "FabricVoiceAssistant";

    /// <summary>
    /// System instructions for the Foundry Agent. Use {AlertContext} placeholder for alert-specific context.
    /// </summary>
    public string AgentInstructions { get; set; } = """
        You are an AI assistant that helps users quickly understand alerts and their impact.
        You have access to live data through a connected Fabric Data Agent.
        When asked to generate an executive summary or answer questions, query the Data Agent for up-to-date information.
        Use clear, professional language suitable for executives.
        Keep spoken summaries to approximately 30 seconds (~75-90 words).
        """;

    /// <summary>
    /// Template for building the executive summary request. Supports placeholders:
    /// - {AlertType}: Type of alert
    /// - {SourceId}: Source identifier
    /// - {SourceName}: Source name/location
    /// - {Severity}: Alert severity
    /// - {Title}: Alert title
    /// - {Description}: Alert description
    /// - {Timestamp}: When the alert occurred
    /// - {Metadata}: Additional metadata as key=value pairs
    /// </summary>
    public string SummaryRequestTemplate { get; set; } = """
        A webhook alert has been triggered. You MUST use the Data Agent tool to answer — do NOT make up data.

        Follow these steps in order:
        1. Query the Data Agent for the current overall production status, any ongoing disruptions, anomalies, or problems.
        2. Then try to find information specifically related to the alert details below (source ID, source name, description).
        3. Generate a concise ~30-second spoken executive summary:
           - If you found data matching the alert: summarize the issue, its impact based on real data, and recommended actions.
           - If no matching data was found for the specific alert: report that a webhook notification was received, but after checking with the production data, no corresponding issue was confirmed. Then briefly summarize the current overall production status you retrieved in step 1.

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
}
