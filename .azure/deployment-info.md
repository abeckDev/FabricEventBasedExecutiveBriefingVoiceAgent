# Smart Factory Demo - Deployment Information

**Deployed:** April 15, 2026  
**Resource Group:** <your-resource-group>  
**Location:** swedencentral

## Azure Resources

| Resource | Name | Purpose |
|----------|------|---------|
| Container App | <your-container-app-name> | Hosts the call agent service |
| Key Vault | kv-bfdbsyzzp3zwq | Secure storage for secrets |
| Azure OpenAI | oai-bfdbsyzzp3zwq | GPT Realtime 1.5 model |
| ACS | acs-bfdbsyzzp3zwq | Communication Services for calls |
| AI Foundry Hub | hub-bfdbsyzzp3zwq | AI services hub |
| AI Foundry Project | proj-bfdbsyzzp3zwq | Agent project workspace |
| Managed Identity | id-bfdbsyzzp3zwq | Container App identity |
| Log Analytics | log-bfdbsyzzp3zwq | Logging and monitoring |

## Configuration

### Phone Numbers
- **Manager Phone:** +1234567890
- **ACS Phone:** +33801150157 (needs to be provisioned in acs-bfdbsyzzp3zwq)

### Endpoints
- **Container App URL:** https://<your-container-app-hostname>
- **Alert Webhook:** https://<your-container-app-hostname>/api/alert
- **Callback URL:** https://<your-container-app-hostname>/api/callbacks
- **WebSocket URL:** wss://<your-container-app-hostname>/ws/audio
- **OpenAI Endpoint:** https://oai-bfdbsyzzp3zwq.openai.azure.com/

### Managed Identity
- **Client ID:** b4d2f012-83a3-4132-9e31-d50f875f57ce

## Microsoft Fabric

- **Workspace:** Saab-ProductionLineDemo
- **Eventhouse:** TBD
- **Data Agent Connection ID:** PENDING_DATA_AGENT_SETUP

## Next Steps

1. ✅ Azure infrastructure deployed
2. ✅ Fabric Eventhouse tables and policies configured
3. ✅ Fabric Data Agent connected to AI Foundry
4. ✅ Application built and deployed
5. **⬜ Test the voice agent with: `./test-webhook.sh`**
6. ⬜ (Optional) Set up Event Hub + Eventstream for full pipeline
7. ⬜ (Optional) Configure Data Activator trigger

## Quick Test

Run the test script to trigger a call:
```bash
./test-webhook.sh
```

Or manually test with curl:
```bash
curl -X POST https://<your-container-app-hostname>/api/alert \
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

## Viewing Logs

Monitor application logs:
```bash
az containerapp logs show --name <your-container-app-name> --resource-group <your-resource-group> --follow
```

## Configuration Summary

**AI Foundry Project:** aifoundry-sandbox  
**Data Agent Connection:** ProductionStatusConsultant  
**GPT-4o Deployment:** gpt-4o  
**Realtime Model:** gpt-realtime  
**Application Image:** albecaisandboxregistry.azurecr.io/smartfactorycallagent:latest  
**Current Revision:** <your-container-app-revision> (Healthy ✅)
