namespace SmartFactoryCallAgent.Configuration;

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
}

public class FoundrySettings
{
    public string ProjectEndpoint { get; set; } = string.Empty;
}

public class FabricSettings
{
    public string KustoEndpoint { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
}
