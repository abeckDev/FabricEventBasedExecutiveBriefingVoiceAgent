# Smart Factory Voice Agent - Debug Summary

## Deployment Status: ✅ Partially Working

**Date:** April 15, 2026  
**Resource Group:** <your-resource-group>  
**Container App:** <your-container-app-name>  
**Current Revision:** <your-container-app-revision>  

## ✅ What's Working

1. **Azure Infrastructure** - All resources deployed successfully
2. **Fabric Eventhouse** - Tables, mappings, and policies configured
3. **Container App** - Application running and healthy
4. **Webhook Endpoint** - Receiving and processing alerts correctly
5. **Basic Call Flow** - App attempts to place calls via ACS

## ⚠️ Issues Identified

### 1. Foundry Agent Authentication [BLOCKED]

**Status:** Requires manual configuration  
**Issue:** The Azure.AI.Projects SDK (`AgentsClient`) doesn't support managed identity authentication from Container Apps in the current version.

**Error:**
```
AuthenticationFailedException: ManagedIdentityCredential authentication failed
```

**Root Cause:**  
- The SDK requires specific managed identity configuration
- Container Apps managed identity isn't being recognized properly
- API key authentication not supported by `AgentsClient` constructor

**Workaround:**  
Application falls back to a static summary message when Foundry Agent fails:
```
"Attention: Machine {machineId} at {station} has triggered a vibration alert at {vibration}g, 
exceeding the threshold. Immediate maintenance inspection is recommended."
```

**To Fix:**  
- Use SDK version that supports container app managed identities
- OR implement direct REST API calls to Azure AI Services endpoints
- OR use service principal authentication

### 2. ACS Phone Number Not Provisioned

**Status:** Configuration issue  
**Issue:** The new ACS resource (`acs-bfdbsyzzp3zwq`) doesn't have a phone number provisioned yet.

**Error:**
```
CreateCallFailed - Call disconnected immediately
```

**Configured Phone:** `+33801150157` (from old ACS resource `AlbecCommunicationServices`)  
**Current ACS:** `acs-bfdbsyzzp3zwq` (new, no phone)

**To Fix:**  
Option A - Provision new phone number:
```bash
# Buy a phone number for the new ACS resource
az communication phonenumber list-available ...
az communication phonenumber purchase ...
```

Option B - Use existing ACS resource:
```bash
# Update container app to use old ACS connection string
az communication list-key --name AlbecCommunicationServices \
  --resource-group CommunicationServices_DEMO
  
# Update the acs-connection-string secret in Key Vault
```

## 📊 Current Behavior

When you trigger the webhook:
1. ✅ Alert received and logged
2. ⚠️ Foundry Agent fails (uses fallback summary)
3. ✅ Summary generated (212 characters)
4. ✅ Call placement attempted
5. ❌ Call fails immediately (no phone number)

## 🧪 Testing

Test the current setup:
```bash
./test-webhook.sh
```

**Expected Response:**
```json
{
  "status": "success",
  "callConnectionId": "xxx-xxx-xxx",
  "execSummary": "Attention: Machine MACHINE-001...",
  "machineId": "MACHINE-001",
  "stationName": "Assembly-A"
}
```

## 🔧 Quick Fixes

### Fix #1: Use Existing ACS (Recommended for Demo)

```bash
# Get existing ACS connection string (requires extension install confirmation)
az communication list-key --name AlbecCommunicationServices \
  --resource-group CommunicationServices_DEMO \
  --query primaryConnectionString -o tsv

# Store in Key Vault
az keyvault secret set \
  --vault-name kv-bfdbsyzzp3zwq \
  --name acs-connection-string \
  --value "<connection-string-from-above>"

# Restart container app
az containerapp revision restart \
  --name <your-container-app-name> \
  --resource-group <your-resource-group> \
  --revision <your-container-app-revision>
```

### Fix #2: Provision Phone Number in New ACS

This requires going through Azure Portal:
1. Go to `acs-bfdbsyzzp3zwq` resource
2. Click "Phone numbers" → "Get"
3. Select country/region and number type
4. Purchase and assign

**Note:** Phone provisioning can take 5-15 minutes

## 📝 Configuration Summary

| Component | Status | Value |
|-----------|--------|-------|
| Webhook URL | ✅ Working | https://<your-container-app-hostname>/api/alert |
| Foundry Endpoint | ✅ Configured | aifoundry-sandbox-resource |
| Data Agent Connection | ✅ Configured | ProductionStatusConsultant |
| Manager Phone | ✅ Configured | +1234567890 |
| ACS Phone | ❌ Not provisioned | +33801150157 (not in new ACS) |
| Eventhouse | ✅ Working | FactoryEventhouse |
| GPT-4o Model | ✅ Deployed | gpt-4o (2024-11-20) |
| Realtime Model | ✅ Deployed | gpt-realtime (2025-08-28) |

## 🎯 Next Steps

**Priority 1 - Get Voice Calls Working:**
- Use existing ACS resource with provisioned phone
- Test end-to-end call flow

**Priority 2 - Fix Foundry Agent:**
- Investigate SDK version compatibility
- Consider REST API implementation
- OR accept fallback summary for demo

**Priority 3 - Optional Enhancements:**
- Set up Event Hub + simulator
- Configure Data Activator trigger
- Add proper error handling and retry logic

## 📞 Support

- **Logs:** `az containerapp logs show --name <your-container-app-name> --resource-group <your-resource-group> --follow`
- **Health:** `az containerapp revision show --name <your-container-app-name> --resource-group <your-resource-group> --revision <your-container-app-revision>`
- **Deployment Info:** See [.azure/deployment-info.md](.azure/deployment-info.md)
