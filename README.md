# Smart Factory Executive Briefing Voice Agent

An event-driven voice agent system that automatically notifies factory managers about machine anomalies via phone call, powered by Microsoft Fabric, Azure AI Foundry, Azure Communication Services, and Azure OpenAI Realtime API.

## Architecture

```
Microsoft Fabric Data Activator
    │ (detects vibration > 1.2g for 60+ seconds)
    │ Webhook POST
    ▼
Azure Container App (SmartFactoryCallAgent)
    ├── POST /api/alert
    │       │
    │       ├── Azure AI Foundry Agent (FoundryAgentService)
    │       │   ├── Fabric Data Agent (grounding tool)
    │       │   │   └── Fabric Eventhouse (KQL queries via Data Agent)
    │       │   └── Generates ~30-second executive summary
    │       │
    │       └── ACS Call Automation
    │           • Places outbound PSTN call to manager
    │
    ├── POST /api/callbacks (ACS event webhooks)
    │       • CallConnected → Play exec summary (TTS)
    │       • PlayCompleted → Start bidirectional audio streaming
    │       • CallDisconnected → Cleanup
    │
    └── WebSocket /ws/audio (bidirectional audio bridge)
            ├── ACS audio → Azure OpenAI Realtime API
            └── OpenAI responses → ACS audio
                • Voice Q&A powered by gpt-4o-realtime
                • Follow-up questions answered via Foundry Agent → Data Agent
```

**Key design principle**: The application is data-access agnostic. All factory data access goes through the Foundry Agent, which uses the Fabric Data Agent as a grounding tool. No direct Kusto/Eventhouse access is needed from the application — no database credentials required.

## Prerequisites

- Azure subscription with the following resources:
  - Azure Communication Services with a PSTN phone number
  - Azure OpenAI with `gpt-4o-realtime-preview` deployment
  - Azure AI Foundry project with a `gpt-4o` deployment
  - Azure Container Apps environment
  - Azure Key Vault
- Microsoft Fabric workspace with:
  - Eventhouse (KQL Database)
  - Eventstream connected to Azure Event Hub
  - Data Activator configured for machine anomaly detection
  - **Fabric Data Agent** exposing the KQL database (see setup below)
- [Azure CLI](https://docs.microsoft.com/en-us/cli/azure/install-azure-cli) installed
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) installed
- [Docker](https://www.docker.com/) installed

## Step-by-Step Setup

### 1. Deploy Azure Infrastructure

```bash
# Login to Azure
az login

# Create a resource group
az group create --name rg-smartfactory --location eastus

# Deploy the Bicep template
az deployment group create \
  --resource-group rg-smartfactory \
  --template-file infra/main.bicep \
  --parameters infra/main.parameters.json \
  --parameters environmentName=smartfactory-prod \
               managerPhoneNumber="+14255550100" \
               acsPhoneNumber="+14255550199" \
               foundryDataAgentConnectionId="<your-data-agent-connection-id>"

# Note the outputs: containerAppUrl, callbackUrl, webSocketUrl, alertWebhookUrl
```

### 2. Set Up Fabric Eventhouse

1. Open your Microsoft Fabric workspace
2. Create a new **Eventhouse** and open the KQL Database
3. Open a new **KQL Queryset** and run the scripts in order:

```bash
# Run each script in the Fabric KQL Queryset:
kql/01_create_tables.kql       # Create all tables
kql/02_enable_streaming.kql    # Enable streaming ingestion
kql/03_create_mapping.kql      # Create JSON ingestion mappings
kql/04_update_policies.kql     # Set up auto-routing update policies
kql/05_seed_supply_risk.kql    # Seed supply risk and order data
```

### 3. Create and Connect the Fabric Data Agent

The application uses a Fabric Data Agent (exposed via AI Foundry) instead of direct Kusto access.

**Step 3a: Create the Fabric Data Agent**

1. In your Microsoft Fabric workspace, open your **Eventhouse** KQL Database
2. Click **New** → **Data Agent** (or navigate to the Data Agent section)
3. Configure the Data Agent to expose the relevant tables (MachineTelemetry, AssemblyEvents, ProductionKPIs, Orders, SupplyRisk)
4. Publish and note the Data Agent endpoint URL

**Step 3b: Connect the Data Agent to AI Foundry**

1. In your Azure AI Foundry project, go to **Settings** → **Connected resources**
2. Click **Add connection** → **Microsoft Fabric**
3. Enter the Fabric Data Agent endpoint URL
4. Complete the connection setup
5. Copy the **Connection ID** (you will need this for configuration)

**Step 3c: Configure the application**

Set `Foundry__DataAgentConnectionId` to the Connection ID copied in the previous step.

### 4. Configure Eventstream

1. In your Fabric workspace, create a new **Eventstream**
2. Add your Azure Event Hub as a **Source**
3. Add the Eventhouse **RawEvents** table as a **Destination**
4. Configure the destination to use the `RawEventsMapping` JSON mapping

### 5. Configure and Run the Simulator

Update the connection string in the simulator before running:

```csharp
// simulator/SmartFactorySimulator/Program.cs
private const string EventHubConnectionString = "<YOUR_EVENT_HUB_CONNECTION_STRING>";
private const string EventHubName = "<YOUR_EVENT_HUB_NAME>";
```

```bash
cd simulator/SmartFactorySimulator
dotnet run
```

The simulator will:
- Send machine telemetry, assembly events, and KPI events every second
- Trigger an anomaly (vibration > 1.2g) every 50 events on MACHINE-001

### 6. Build and Deploy the Container App

```bash
# Build Docker image
cd src/SmartFactoryCallAgent
docker build -t smartfactorycallagent:latest .

# Tag and push to GitHub Container Registry (or Azure Container Registry)
docker tag smartfactorycallagent:latest ghcr.io/YOUR_ORG/smartfactorycallagent:latest
docker push ghcr.io/YOUR_ORG/smartfactorycallagent:latest

# Update the container app with the new image
az containerapp update \
  --name ca-YOURTOKEN \
  --resource-group rg-smartfactory \
  --image ghcr.io/YOUR_ORG/smartfactorycallagent:latest
```

### 7. Configure Data Activator

1. In Microsoft Fabric, open **Data Activator**
2. Create a new **Reflex** item
3. Connect it to your Eventstream as a data source
4. Create a trigger on `MachineTelemetry`:
   - **Condition**: `Vibration > 1.2` sustained for **60 seconds**
5. Set the **Action** to **Webhook**:
   - **URL**: `https://YOUR_CONTAINER_APP/api/alert`
   - **Method**: POST
   - **Body**: Include MachineId, StationName, Vibration, Temperature, Timestamp, OrderId

### 8. Test End-to-End

```bash
# Test the webhook manually
curl -X POST https://YOUR_CONTAINER_APP/api/alert \
  -H "Content-Type: application/json" \
  -d '{
    "machineId": "MACHINE-001",
    "stationName": "Assembly-A",
    "vibration": 1.45,
    "temperature": 87.3,
    "timestamp": "2024-01-15T14:30:00Z",
    "orderId": "ORD-2024-001",
    "alertType": "VibrationAnomaly",
    "severity": "High"
  }'
```

## Configuration Reference

| Setting | Environment Variable | Description |
|---------|---------------------|-------------|
| ACS Connection String | `Acs__ConnectionString` | Azure Communication Services connection string |
| ACS Phone Number | `Acs__PhoneNumber` | Your ACS PSTN phone number (E.164) |
| Callback Base URL | `Acs__CallbackBaseUrl` | Base URL of the Container App |
| OpenAI Endpoint | `OpenAi__Endpoint` | Azure OpenAI endpoint URL |
| OpenAI API Key | `OpenAi__ApiKey` | Azure OpenAI API key |
| OpenAI Deployment | `OpenAi__DeploymentName` | Deployment name (default: gpt-4o-realtime) |
| Foundry Project Endpoint | `Foundry__ProjectEndpoint` | AI Foundry project discovery URL |
| Foundry Model Deployment | `Foundry__ModelDeploymentName` | Model deployment name for the agent (default: gpt-4o) |
| Data Agent Connection ID | `Foundry__DataAgentConnectionId` | Connection ID of the Fabric Data Agent in AI Foundry |
| Manager Phone | `ManagerPhoneNumber` | Manager's phone number for outbound calls (E.164) |

## Local Development

```bash
# Install dependencies
cd src/SmartFactoryCallAgent
dotnet restore

# Set environment variables (or use appsettings.Development.json)
export Acs__ConnectionString="<YOUR_CONNECTION_STRING>"
export Acs__PhoneNumber="+14255550199"
export Acs__CallbackBaseUrl="https://your-ngrok-url.ngrok.io"
export OpenAi__Endpoint="https://your-openai.openai.azure.com/"
export OpenAi__ApiKey="<YOUR_API_KEY>"
export Foundry__ProjectEndpoint="https://your-project.api.azureml.ms"
export Foundry__ModelDeploymentName="gpt-4o"
export Foundry__DataAgentConnectionId="<YOUR_FABRIC_DATA_AGENT_CONNECTION_ID>"
export ManagerPhoneNumber="+14255550100"

# Run the app
dotnet run

# For local webhook testing, use ngrok to expose port 5000
ngrok http 5000
# Update Acs__CallbackBaseUrl with the ngrok URL
```

## Project Structure

```
/
├── README.md                                    # This file
├── src/
│   └── SmartFactoryCallAgent/
│       ├── SmartFactoryCallAgent.csproj          # .NET 8 ASP.NET Core project
│       ├── Program.cs                            # Host builder, DI, WebSocket middleware
│       ├── Dockerfile                            # Multi-stage Docker build
│       ├── appsettings.json                      # Configuration template
│       ├── Controllers/
│       │   ├── AlertController.cs                # POST /api/alert
│       │   └── CallbackController.cs             # POST /api/callbacks
│       ├── Services/
│       │   ├── CallService.cs                    # ACS Call Automation
│       │   ├── FoundryAgentService.cs            # Azure AI Foundry Agent (+ Data Agent grounding)
│       │   └── AudioStreamingHandler.cs          # WebSocket audio bridge
│       ├── Models/
│       │   ├── DataActivatorAlert.cs             # Alert payload model
│       │   └── CallContextStore.cs               # In-memory call state
│       └── Configuration/
│           └── AppSettings.cs                    # Strongly-typed settings
├── infra/
│   ├── main.bicep                                # Azure Bicep template
│   └── main.parameters.json                      # Bicep parameters
├── kql/
│   ├── 01_create_tables.kql                      # Table definitions
│   ├── 02_enable_streaming.kql                   # Enable streaming ingestion
│   ├── 03_create_mapping.kql                     # JSON ingestion mappings
│   ├── 04_update_policies.kql                    # Update policies
│   └── 05_seed_supply_risk.kql                   # Seed data
├── simulator/
│   └── SmartFactorySimulator/
│       ├── SmartFactorySimulator.csproj           # .NET 8 console app
│       └── Program.cs                             # Event Hub simulator
└── .github/
    └── workflows/
        └── build.yml                              # CI: build + Docker push
```

## Troubleshooting

### Call not being placed
- Verify `Acs__ConnectionString` is correct and the ACS resource has a PSTN phone number
- Check Container App logs: `az containerapp logs show --name ca-YOURTOKEN --resource-group rg-smartfactory`
- Ensure `Acs__CallbackBaseUrl` is publicly accessible (Container App ingress is enabled)

### No audio / TTS not playing
- Verify the ACS callback URL is reachable from ACS servers
- Check `CallbackController` logs for event handling errors
- Ensure the ACS phone number is provisioned for voice calls

### Foundry Agent not returning data
- Verify `Foundry__DataAgentConnectionId` is set to a valid connection ID in your AI Foundry project
- Ensure the Fabric Data Agent is published and the connection is active in AI Foundry
- Check that the AI Foundry project managed identity has access to the Fabric workspace
- Verify `Foundry__ProjectEndpoint` is the correct AI Foundry project endpoint

### OpenAI Realtime connection failing
- Verify `gpt-4o-realtime-preview` deployment exists and is active
- Check API key and endpoint format
- Ensure WebSocket connections are allowed through your network/firewall

### Data Activator not triggering
- Verify the Reflex is monitoring the correct stream and field
- Check that the alert threshold (vibration > 1.2g for 60s) is being breached
- Test by sending a manual POST to `/api/alert` (see step 8 above)

## License

MIT License - see [LICENSE](LICENSE) for details.
