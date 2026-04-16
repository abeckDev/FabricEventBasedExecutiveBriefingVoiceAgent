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
    /// AI Foundry project endpoint (discovery URL)
    /// </summary>
    public string ProjectEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// AI Foundry project connection string (format: endpoint;subscription;resourcegroup;project)
    /// If provided, this takes precedence over constructing from ProjectEndpoint
    /// </summary>
    public string? ProjectConnectionString { get; set; }

    /// <summary>
    /// Model deployment name for the Foundry Agent (e.g., gpt-4o)
    /// </summary>
    public string ModelDeploymentName { get; set; } = "gpt-4o";

    /// <summary>
    /// Connection ID of the Fabric Data Agent in the AI Foundry project
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
        An alert has been triggered. Please:
        1. Query the Data Agent for relevant context and recent data
        2. Generate a concise ~30-second spoken executive summary covering: the alert, impact assessment, and recommended immediate actions.

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
