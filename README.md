# Fabric Voice Call Agent

A reusable building block for Microsoft Fabric demos that require automated voice calling capabilities. This solution enables event-driven voice notifications powered by Microsoft Fabric, Azure AI Foundry, Azure Communication Services, and Azure OpenAI Realtime API.

## Overview

This is a **generic, demo-independent** voice calling framework designed to work with any Fabric-based scenario. When an alert is triggered (from Fabric Data Activator, a Notebook, or any webhook source), the system:

1. **Receives an alert** via HTTP webhook
2. **Queries Fabric data** through an AI Foundry Agent connected to a Fabric Data Agent
3. **Generates an executive summary** with context from your Fabric data
4. **Places an outbound phone call** using Azure Communication Services
5. **Delivers the summary** using Azure OpenAI Realtime voice
6. **Handles follow-up questions** via real-time voice conversation

## Architecture

```
Any Alert Source (Data Activator, Notebook, Custom Webhook)
    │ HTTP POST /api/alert
    ▼
Azure Container App (FabricVoiceCallAgent)
    ├── Receives generic AlertPayload
    │       │
    │       ├── Azure AI Foundry Agent
    │       │   ├── Fabric Data Agent (grounding tool)
    │       │   │   └── Your Fabric Data (Eventhouse, Lakehouse, etc.)
    │       │   └── Generates ~30-second executive summary
    │       │
    │       └── ACS Call Automation
    │           • Places outbound PSTN call
    │
    ├── POST /api/callbacks (ACS event webhooks)
    │       • CallConnected → Start bidirectional audio
    │       • CallDisconnected → Cleanup
    │
    └── WebSocket /ws/audio (bidirectional audio bridge)
            ├── ACS audio → Azure OpenAI Realtime API
            └── OpenAI responses → ACS audio
                • Voice Q&A powered by gpt-4o-realtime
                • Follow-up questions via Foundry Agent → Data Agent
```

## Key Features

- **Generic Alert Payload**: Accepts flexible alert data with metadata dictionary for domain-specific fields
- **Configurable Prompts**: Customize agent instructions and system prompts via configuration
- **Domain Agnostic**: Works with any Fabric data scenario (manufacturing, healthcare, retail, finance, etc.)
- **Real-time Voice**: Two-way voice conversation for follow-up questions
- **Data Grounding**: All responses are grounded in your actual Fabric data

## Demo Scenarios

This building block can power voice calling for various Fabric demos:

| Scenario | Alert Source | Data Source | Use Case |
|----------|--------------|-------------|----------|
| Smart Factory | Data Activator (telemetry) | Eventhouse (machine data) | Equipment anomaly notifications |
| Retail | Data Activator (inventory) | SQL Analytics Endpoint | Stock level warnings |
| Finance | Notebook (fraud detection) | Eventhouse (transactions) | Suspicious activity alerts |

## Quick Start

### Prerequisites

- Azure subscription with:
  - Azure Communication Services (with PSTN phone number)
  - Azure OpenAI (`gpt-4o-realtime-preview` deployment)
  - Azure AI Foundry project
  - Azure Container Apps
- Microsoft Fabric workspace with:
  - Your data (Eventhouse, Lakehouse, etc.)
  - Fabric Data Agent configured and connected to AI Foundry

### 1. Deploy Infrastructure

```bash
az deployment group create \
  --resource-group rg-fabric-voice-demo \
  --template-file infra/main.bicep \
  --parameters infra/main.parameters.json \
  --parameters environmentName=fabric-voice-demo \
               defaultPhoneNumber="+14255550100" \
               acsPhoneNumber="+14255550199" \
               foundryDataAgentConnectionId="<your-data-agent-connection-id>"
```

### 2. Configure for Your Scenario

Update `appsettings.json` or environment variables to customize the agent behavior:

```json
{
  "Foundry": {
    "AgentInstructions": "You are an AI assistant for [YOUR DOMAIN]. You have access to live data through a connected Fabric Data Agent...",
    "SummaryRequestTemplate": "An alert has been triggered. Please query the Data Agent for [YOUR SPECIFIC QUERIES]..."
  },
  "VoiceAgent": {
    "SystemPromptTemplate": "You are calling a user about [YOUR SCENARIO]...",
    "DataAssistantToolDescription": "Ask questions about [YOUR DATA DOMAIN]..."
  }
}
```

### 3. Send an Alert

```bash
curl -X POST https://YOUR_APP/api/alert \
  -H "Content-Type: application/json" \
  -d '{
    "sourceId": "SENSOR-001",
    "sourceName": "Production Line A",
    "alertType": "Threshold",
    "severity": "High",
    "title": "Temperature Exceeded",
    "description": "Temperature sensor reading above critical threshold",
    "timestamp": "2024-01-15T14:30:00Z",
    "phoneNumber": "+14255550100",
    "metadata": {
      "temperature": 95.5,
      "threshold": 85.0,
      "duration_minutes": 15
    }
  }'
```

## Alert Payload Schema

The `AlertPayload` model is designed to be flexible:

| Field | Type | Description |
|-------|------|-------------|
| `sourceId` | string | Unique identifier (machine ID, sensor ID, etc.) |
| `sourceName` | string | Human-readable name/location |
| `alertType` | string | Type of alert (Threshold, Anomaly, Critical, etc.) |
| `severity` | string | Severity level (Low, Medium, High, Critical) |
| `title` | string | Short title for the alert |
| `description` | string | Detailed description |
| `timestamp` | datetime | When the alert occurred |
| `phoneNumber` | string | Override phone number (optional) |
| `metadata` | object | Domain-specific key-value pairs |

## Configuration Reference

| Setting | Environment Variable | Description |
|---------|---------------------|-------------|
| ACS Connection String | `Acs__ConnectionString` | Azure Communication Services connection string |
| ACS Phone Number | `Acs__PhoneNumber` | Your ACS PSTN phone number (E.164) |
| Callback Base URL | `Acs__CallbackBaseUrl` | Base URL of the Container App |
| OpenAI Endpoint | `OpenAi__Endpoint` | Azure OpenAI endpoint URL |
| OpenAI API Key | `OpenAi__ApiKey` | Azure OpenAI API key (optional with managed identity) |
| OpenAI Deployment | `OpenAi__DeploymentName` | Deployment name (default: gpt-4o-realtime) |
| OpenAI Voice | `OpenAi__Voice` | Voice for TTS (default: alloy) |
| Foundry Project Endpoint | `Foundry__ProjectEndpoint` | AI Foundry project discovery URL |
| Foundry Connection String | `Foundry__ProjectConnectionString` | Full connection string (alternative) |
| Data Agent Connection ID | `Foundry__DataAgentConnectionId` | Connection ID of Fabric Data Agent |
| Agent Name | `Foundry__AgentName` | Name for the Foundry Agent |
| Agent Instructions | `Foundry__AgentInstructions` | System instructions for the agent |
| Summary Template | `Foundry__SummaryRequestTemplate` | Template for generating summaries |
| Default Phone | `VoiceAgent__DefaultPhoneNumber` | Default phone number for calls |
| Managed Identity | `VoiceAgent__ManagedIdentityClientId` | Client ID for managed identity auth |
| System Prompt | `VoiceAgent__SystemPromptTemplate` | Template for voice agent prompts |
| Tool Description | `VoiceAgent__DataAssistantToolDescription` | Description for data query tool |

## Connecting to Fabric Data

### 1. Create a Fabric Data Agent

1. In your Fabric workspace, open your data source (Eventhouse, Lakehouse, etc.)
2. Create a new **Data Agent** that exposes your relevant tables
3. Publish and note the Data Agent endpoint

### 2. Connect to AI Foundry

1. In Azure AI Foundry, go to **Settings** → **Connected resources**
2. Add a **Microsoft Fabric** connection
3. Copy the **Connection ID**
4. Set `Foundry__DataAgentConnectionId` to this value

## Triggering from Fabric

### From Data Activator

Configure a Reflex with a webhook action pointing to `/api/alert`:

```json
{
  "sourceId": "{{MachineId}}",
  "sourceName": "{{StationName}}",
  "alertType": "Threshold",
  "severity": "{{Severity}}",
  "timestamp": "{{Timestamp}}",
  "metadata": {
    "metric": "{{MetricValue}}"
  }
}
```

### From a Notebook

```python
import requests

alert = {
    "sourceId": "ANALYSIS-001",
    "sourceName": "Fraud Detection Model",
    "alertType": "Anomaly",
    "severity": "Critical",
    "title": "Suspicious Transaction Pattern Detected",
    "description": "Multiple high-value transactions from unusual locations",
    "phoneNumber": "+14255550100",
    "metadata": {
        "transaction_count": 15,
        "total_value": 50000,
        "risk_score": 0.95
    }
}

response = requests.post(
    "https://YOUR_APP/api/alert",
    json=alert
)
```

## Local Development

```bash
cd src/FabricVoiceCallAgent
dotnet restore
dotnet run

# For webhook testing, use ngrok
ngrok http 5000
# Update Acs__CallbackBaseUrl with ngrok URL
```

## Project Structure

```
/
├── src/FabricVoiceCallAgent/           # Main application
│   ├── Controllers/
│   │   ├── AlertController.cs          # POST /api/alert
│   │   └── CallbackController.cs       # POST /api/callbacks
│   ├── Services/
│   │   ├── CallService.cs              # ACS Call Automation
│   │   ├── FoundryAgentService.cs      # AI Foundry Agent
│   │   └── AudioStreamingHandler.cs    # WebSocket audio bridge
│   ├── Models/
│   │   ├── AlertPayload.cs             # Generic alert model
│   │   └── CallContextStore.cs         # Call state management
│   └── Configuration/
│       └── AppSettings.cs              # Configuration classes
├── simulator/AlertSimulator/           # Test event generator
├── infra/                              # Azure Bicep templates
└── kql/                                # Sample KQL scripts
```

## Troubleshooting

### Call not being placed
- Verify ACS connection string and PSTN phone number
- Check that callback URL is publicly accessible
- Review Container App logs

### No audio / voice not working
- Verify OpenAI Realtime deployment exists
- Check managed identity or API key configuration
- Ensure WebSocket connections are allowed

### Foundry Agent not responding
- Verify Data Agent connection ID is correct
- Check AI Foundry project endpoint
- Ensure managed identity has access to Fabric workspace

## License

MIT License - see [LICENSE](LICENSE) for details.
