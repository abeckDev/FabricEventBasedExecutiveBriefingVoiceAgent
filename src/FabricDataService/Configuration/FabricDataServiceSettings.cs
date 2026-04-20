namespace FabricDataService.Configuration;

public class FabricDataServiceSettings
{
    /// <summary>
    /// Azure AI Foundry project endpoint.
    /// Form: https://&lt;resource&gt;.services.ai.azure.com/api/projects/&lt;project&gt;
    /// </summary>
    public string ProjectEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// ID of a pre-provisioned Foundry Agent that has a Fabric Data Agent tool attached.
    /// Required — the service does not auto-create agents.
    /// </summary>
    public string AgentId { get; set; } = string.Empty;

    /// <summary>
    /// Client ID for user-assigned managed identity authentication.
    /// Leave empty to use DefaultAzureCredential.
    /// </summary>
    public string? ManagedIdentityClientId { get; set; }

    /// <summary>
    /// Maximum time in seconds to wait for an agent run to complete.
    /// </summary>
    public int RunTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Polling interval in milliseconds when waiting for an agent run.
    /// </summary>
    public int RunPollingIntervalMs { get; set; } = 500;

    /// <summary>
    /// When true, raw tool output is included in server-side logs (may contain sensitive data).
    /// </summary>
    public bool EnableDebugToolLogging { get; set; } = false;
}
