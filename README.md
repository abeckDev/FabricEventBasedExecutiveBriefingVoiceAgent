# Fabric Event-Based Executive Briefing Voice Agent

A reusable building block for Microsoft Fabric demos that require automated voice calling capabilities. This solution enables event-driven voice notifications powered by Microsoft Fabric, Azure AI Foundry, Azure Communication Services, and Azure OpenAI Realtime API.

## Overview

This is a **generic, demo-independent** voice calling framework designed to work with any Fabric-based scenario. When an alert is triggered (from Fabric Data Activator, a Notebook, or any webhook source), the system:

1. **Receives an alert** via HTTP webhook
2. **Queries Fabric data** through the FabricDataService backend (AI Foundry Agent + Fabric Data Agent)
3. **Generates an executive summary** with context from your Fabric data
4. **Places an outbound phone call** using Azure Communication Services
5. **Delivers the summary** using Azure OpenAI Realtime voice
6. **Handles follow-up questions** via real-time voice conversation backed by live data

## Architecture

The solution consists of **two decoupled services** for easier maintenance, debugging, and reusability:

```
Any Alert Source (Data Activator, Notebook, Custom Webhook)
    │ HTTP POST /api/alert
    ▼
┌─────────────────────────────────────────────────────────┐
│  FabricVoiceCallAgent (external ingress)                │
│  ├── Receives AlertPayload                              │
│  ├── Calls FabricDataService for exec summary ──────────┼──┐
│  ├── Places PSTN call via ACS Call Automation            │  │
│  └── WebSocket /ws/audio (bidirectional audio bridge)    │  │
│      ├── ACS audio ↔ Azure OpenAI Realtime API          │  │
│      └── Follow-up Q&A → FabricDataService ─────────────┼──┤
└─────────────────────────────────────────────────────────┘  │
                                                             │
┌─────────────────────────────────────────────────────────┐  │
│  FabricDataService (internal ingress)                   │◄─┘
│  ├── POST /ask — stateless query endpoint               │
│  ├── AI Foundry Agent (Azure.AI.Projects v2 SDK)        │
│  │   └── Fabric Data Agent (grounding tool)             │
│  │       └── Your Fabric Data (Eventhouse, Lakehouse)   │
│  ├── GET /health/live — liveness probe                  │
│  └── GET /health/ready — readiness probe (agent check)  │
└─────────────────────────────────────────────────────────┘
```

### Why Two Services?

| Concern | Benefit |
|---------|---------|
| **Decoupled SDKs** | Backend uses Azure.AI.Projects v2 GA; voice agent uses ACS + OpenAI Realtime — no SDK conflicts |
| **Independent scaling** | Backend can scale to multiple replicas; voice agent is single-replica (in-memory call state) |
| **Reusability** | FabricDataService can be consumed by other apps (Copilot Studio, Power Automate, MCP) |
| **Debuggability** | Test data queries independently from voice/call issues |

## Key Features

- **Generic Alert Payload**: Accepts flexible alert data with metadata dictionary for domain-specific fields
- **Configurable Prompts**: Customize agent instructions and system prompts via configuration
- **Domain Agnostic**: Works with any Fabric data scenario (manufacturing, healthcare, retail, finance, etc.)
- **Real-time Voice**: Two-way voice conversation for follow-up questions
- **Data Grounding**: All responses are grounded in your actual Fabric data
- **Separate Timeouts**: 60s for initial summary generation, 20s for live follow-up questions

## Quick Start

### Prerequisites

- Azure subscription with:
  - Azure Communication Services (with PSTN phone number)
  - Azure OpenAI (with a `gpt-4o-realtime` deployment)
  - Azure AI Foundry project with a configured Agent
  - Azure Container Apps
- Microsoft Fabric workspace with:
  - Your data (Eventhouse, Lakehouse, etc.)
  - Fabric Data Agent configured and connected to AI Foundry
- .NET 8 SDK (for local development)

### 1. Deploy Infrastructure

```bash
cp deploy.env.template deploy.env
# Edit deploy.env with your values (see Configuration Reference below)

./deploy.sh
```

The script deploys both services to the same Container Apps Environment:
- **FabricDataService** — internal ingress, backend MI with AI Foundry RBAC
- **FabricVoiceCallAgent** — external ingress, voice MI with ACS/OpenAI access

### 2. Configure for Your Scenario

Set the Foundry agent name in `deploy.env` (or as environment variables):

```bash
# The agent must already exist in AI Foundry portal
FOUNDRY_PROJECT_ENDPOINT="https://<resource>.services.ai.azure.com/api/projects/<project>"
FOUNDRY_AGENT_NAME="MyFabricAssistant"
```

### 3. Send an Alert

```bash
curl -X POST https://YOUR_VOICE_APP/api/alert \
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

## Local Development & Testing

### Running Both Services Locally

```bash
# Terminal 1: Start FabricDataService (port 5100)
cd src/FabricDataService
dotnet run --urls "http://localhost:5100"

# Terminal 2: Start FabricVoiceCallAgent (port 5000)
cd src/FabricVoiceCallAgent
dotnet run --urls "http://localhost:5000"
```

### Testing the Backend Independently

The FabricDataService can be tested in isolation — no phone calls needed:

```bash
# Health check
curl http://localhost:5100/health/live

# Readiness (verifies agent connectivity)
curl http://localhost:5100/health/ready

# Ask a question
curl -X POST http://localhost:5100/ask \
  -H "Content-Type: application/json" \
  -d '{
    "question": "What is the current production status?",
    "alertContext": {
      "sourceId": "SENSOR-001",
      "severity": "High"
    }
  }'
```

### Testing the Voice Agent

For the voice agent to receive ACS callbacks, you need a publicly accessible URL:

```bash
# Use ngrok or similar
ngrok http 5000

# Then set the callback URL
export Acs__CallbackBaseUrl="https://your-ngrok-url.ngrok.io"
export FabricBackend__BaseUrl="http://localhost:5100"
```

### Testing End-to-End Without Deploying

1. Start FabricDataService locally
2. Verify data queries work via `POST /ask`
3. Start FabricVoiceCallAgent with `FabricBackend__BaseUrl=http://localhost:5100`
4. Use ngrok for ACS callbacks
5. Send an alert to trigger the full flow

## Alert Payload Schema

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

### FabricDataService

| Setting | Environment Variable | Description |
|---------|---------------------|-------------|
| Project Endpoint | `FabricDataService__ProjectEndpoint` | AI Foundry project endpoint |
| Agent Name | `FabricDataService__AgentId` | Agent name in AI Foundry portal |
| Managed Identity | `FabricDataService__ManagedIdentityClientId` | Client ID for Azure auth |
| Timeout | `FabricDataService__QueryTimeoutSeconds` | Agent query timeout (default: 120) |
| Debug Mode | `FabricDataService__IncludeDebugInfo` | Include tool call details in response |

### FabricVoiceCallAgent

| Setting | Environment Variable | Description |
|---------|---------------------|-------------|
| Backend URL | `FabricBackend__BaseUrl` | FabricDataService URL (e.g., `http://fabricdataservice:8080`) |
| Default Timeout | `FabricBackend__DefaultTimeoutSeconds` | Timeout for exec summary calls (default: 60) |
| Follow-up Timeout | `FabricBackend__FollowUpTimeoutSeconds` | Timeout for live call Q&A (default: 20) |
| ACS Connection | `Acs__ConnectionString` | Azure Communication Services connection string |
| ACS Phone | `Acs__PhoneNumber` | Outbound caller ID (E.164) |
| Callback URL | `Acs__CallbackBaseUrl` | Public URL for ACS event webhooks |
| OpenAI Endpoint | `OpenAi__Endpoint` | Azure OpenAI endpoint |
| OpenAI API Key | `OpenAi__ApiKey` | API key (optional with managed identity) |
| OpenAI Deployment | `OpenAi__DeploymentName` | Realtime deployment (default: gpt-realtime) |
| OpenAI Voice | `OpenAi__Voice` | TTS voice (default: alloy) |
| Default Phone | `VoiceAgent__DefaultPhoneNumber` | Default phone number for calls |
| Managed Identity | `VoiceAgent__ManagedIdentityClientId` | Client ID for Azure auth |
| System Prompt | `VoiceAgent__SystemPromptTemplate` | Voice agent prompt (use `{ExecSummary}` placeholder) |

## Connecting to Fabric Data

### 1. Create a Fabric Data Agent

1. In your Fabric workspace, open your data source (Eventhouse, Lakehouse, etc.)
2. Create a new **Data Agent** that exposes your relevant tables
3. Publish the Data Agent

### 2. Create an AI Foundry Agent

1. In Azure AI Foundry portal, create a new **Agent**
2. Add a **Microsoft Fabric** tool connection pointing to your Data Agent
3. Configure the agent's instructions for your domain
4. Note the **agent name** — this is what you'll set as `FOUNDRY_AGENT_NAME`

### 3. Grant Fabric Permissions to Backend Managed Identity

The FabricDataService's managed identity must have access to:
- **AI Foundry project**: `Azure AI User` role (granted by deploy.sh)
- **Fabric workspace**: At least **Viewer** role

In the Fabric portal:
1. Open your **Fabric workspace** → **Manage access**
2. Add the backend managed identity (by Client ID or name) with **Viewer** role

> **Important:** Without Fabric workspace access, the agent can connect to the Data Agent but cannot retrieve data.

## Project Structure

```
/
├── deploy.sh                           # Deploys both services
├── deploy.env.template                 # Configuration template
├── test-webhook.sh                     # Test script
├── src/
│   ├── FabricDataService/              # Backend: Fabric data queries
│   │   ├── Endpoints/AskEndpoint.cs    # POST /ask, health checks
│   │   ├── Services/
│   │   │   ├── IDataQueryService.cs    # Query interface (swappable)
│   │   │   ├── FoundryAgentQueryService.cs  # AI Foundry v2 SDK
│   │   │   └── DirectKustoQueryService.cs   # Kusto fallback (stub)
│   │   ├── Models/                     # AskRequest, AskResponse, domain types
│   │   ├── Configuration/             # FabricDataServiceSettings
│   │   └── Dockerfile
│   └── FabricVoiceCallAgent/           # Voice: ACS calling + OpenAI Realtime
│       ├── Controllers/
│       │   └── AlertController.cs      # POST /api/alert
│       ├── Services/
│       │   ├── FabricBackendClient.cs  # HTTP client → FabricDataService
│       │   ├── CallService.cs          # ACS Call Automation
│       │   └── AudioStreamingHandler.cs # WebSocket audio bridge
│       ├── Models/
│       │   ├── AlertPayload.cs         # Generic alert model
│       │   └── CallContextStore.cs     # In-memory call state (single replica)
│       ├── Configuration/AppSettings.cs
│       └── Dockerfile
└── README.md
```

## Troubleshooting

### FabricDataService issues

| Symptom | Check |
|---------|-------|
| `/health/ready` returns unhealthy | Verify `FabricDataService__ProjectEndpoint` and `FabricDataService__AgentId` |
| Agent returns empty answers | Check managed identity has `Azure AI User` on AI Foundry project |
| "couldn't retrieve data" | Add backend MI to Fabric workspace with Viewer role |
| Timeout on `/ask` | Increase `QueryTimeoutSeconds`; check agent complexity |

### Voice Agent issues

| Symptom | Check |
|---------|-------|
| Call not placed | Verify ACS connection string, PSTN number, and callback URL accessibility |
| No audio | Verify OpenAI Realtime deployment; check API key or MI auth |
| "unable to retrieve information" during call | Check FabricDataService is running; verify `FabricBackend__BaseUrl` |
| Follow-up answers slow | Adjust `FabricBackend__FollowUpTimeoutSeconds` |

### General

- Review Container App logs: `az containerapp logs show --name <app> --resource-group <rg>`
- Test backend independently with `curl` before debugging voice issues
- Check both managed identities have the correct RBAC roles

## Known Limitations

- **Single replica for voice agent**: The `CallContextStore` uses in-memory state. Running multiple replicas will cause call context mismatches. Scale the backend independently instead.
- **No service-to-service auth**: The internal FabricDataService endpoint is protected by Container Apps Environment networking only. For production, add API key or managed identity auth.

## Future Roadmap

- **VoiceLive migration**: Replace WebSocket audio bridge with Azure VoiceLive API
- **MCP exposure**: Expose FabricDataService as an MCP server for broader tool integration
- **Redis state**: Replace in-memory CallContextStore with Azure Cache for Redis

## License

MIT License - see [LICENSE](LICENSE) for details.
