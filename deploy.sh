#!/usr/bin/env bash
###############################################################################
# deploy.sh – CLI-based deployment for Fabric Data Service + Voice Call Agent
#
# Deploys all required Azure resources, builds two container images, and
# configures two Container Apps:
#   1. FabricDataService  – internal ingress, queries Fabric via AI Foundry
#   2. FabricVoiceCallAgent – external ingress, ACS calling + OpenAI Realtime
#
# Usage:
#   ./deploy.sh              # interactive – prompts for missing values
#   ./deploy.sh --no-prompt  # non-interactive – fails on missing values
#
# Configuration is stored in deploy.env (auto-created on first run).
# The script is idempotent – re-running it will update existing resources.
###############################################################################
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INTERACTIVE=true
[[ "${1:-}" == "--no-prompt" ]] && INTERACTIVE=false

# ─── Helper: prompt for a value and persist it to deploy.env ─────────────────
# Usage: prompt_var VAR_NAME "Prompt text" ["default_value"]
prompt_var() {
  local var_name="$1" prompt_text="$2" default="${3:-}"
  local current="${!var_name:-}"

  # Already set and non-empty → nothing to do
  if [[ -n "$current" ]]; then
    return 0
  fi

  if [[ "$INTERACTIVE" == false ]]; then
    if [[ -n "$default" ]]; then
      eval "$var_name=\"\$default\""
    else
      echo "ERROR: Required variable $var_name is not set in deploy.env (use interactive mode or set it manually)"
      exit 1
    fi
  else
    local input
    if [[ -n "$default" ]]; then
      read -rp "  $prompt_text [$default]: " input
      input="${input:-$default}"
    else
      while true; do
        read -rp "  $prompt_text: " input
        [[ -n "$input" ]] && break
        echo "    ⚠ This value is required."
      done
    fi
    eval "$var_name=\"\$input\""
  fi

  # Persist to deploy.env
  if grep -q "^${var_name}=" "$ENV_FILE" 2>/dev/null; then
    # Update existing line — use a different delimiter to avoid issues with
    # slashes and special chars in the value
    local escaped_value
    escaped_value=$(printf '%s' "${!var_name}" | sed 's/[&/\]/\\&/g')
    sed -i "s|^${var_name}=.*|${var_name}=\"${escaped_value}\"|" "$ENV_FILE"
  else
    echo "${var_name}=\"${!var_name}\"" >> "$ENV_FILE"
  fi
}

# ─── Load / create configuration ────────────────────────────────────────────
ENV_FILE="${SCRIPT_DIR}/deploy.env"
if [[ ! -f "$ENV_FILE" ]]; then
  if [[ -f "${SCRIPT_DIR}/deploy.env.template" ]]; then
    echo "Creating deploy.env from template..."
    cp "${SCRIPT_DIR}/deploy.env.template" "$ENV_FILE"
  else
    echo "Creating empty deploy.env..."
    touch "$ENV_FILE"
  fi
fi
# shellcheck disable=SC1090
source "$ENV_FILE"

# ─── Collect required values (prompt if missing) ────────────────────────────
echo ""
echo "Checking configuration..."

prompt_var ENVIRONMENT_NAME    "Environment name (used in resource naming)"    "fabric-voice-demo"
prompt_var RESOURCE_GROUP      "Azure Resource Group name"                     "FabricVoiceConnector_RG"
prompt_var LOCATION            "Azure region"                                  "swedencentral"
prompt_var DEFAULT_PHONE_NUMBER "Default phone number to call (E.164, e.g. +14255550100)"
prompt_var ACS_PHONE_NUMBER    "ACS outbound caller ID phone number (E.164, e.g. +14255550199)"

# Optional: ACS reuse
if [[ -z "${EXISTING_ACS_CONNECTION_STRING:-}" ]] && [[ "$INTERACTIVE" == true ]]; then
  read -rp "  Reuse an existing ACS resource? (y/N): " reuse_acs
  if [[ "$reuse_acs" =~ ^[Yy] ]]; then
    prompt_var EXISTING_ACS_CONNECTION_STRING "ACS connection string"
  fi
fi

# Optional: OpenAI reuse
if [[ -z "${EXISTING_OPENAI_ENDPOINT:-}" ]] && [[ "$INTERACTIVE" == true ]]; then
  read -rp "  Reuse an existing Azure OpenAI resource? (y/N): " reuse_oai
  if [[ "$reuse_oai" =~ ^[Yy] ]]; then
    prompt_var EXISTING_OPENAI_ENDPOINT "Azure OpenAI base endpoint (e.g. https://myresource.openai.azure.com)"
    prompt_var EXISTING_OPENAI_API_KEY  "Azure OpenAI API key"
    read -rp "  OpenAI resource ID for RBAC (leave blank to skip): " oai_rid
    if [[ -n "$oai_rid" ]]; then
      EXISTING_OPENAI_RESOURCE_ID="$oai_rid"
      if grep -q "^EXISTING_OPENAI_RESOURCE_ID=" "$ENV_FILE" 2>/dev/null; then
        sed -i "s|^EXISTING_OPENAI_RESOURCE_ID=.*|EXISTING_OPENAI_RESOURCE_ID=\"${oai_rid}\"|" "$ENV_FILE"
      else
        echo "EXISTING_OPENAI_RESOURCE_ID=\"${oai_rid}\"" >> "$ENV_FILE"
      fi
    fi
  fi
fi

# Optional: Fabric Data Service backend (AI Foundry + Data Agent)
if [[ -z "${FOUNDRY_PROJECT_ENDPOINT:-}" ]] && [[ "$INTERACTIVE" == true ]]; then
  read -rp "  Configure AI Foundry / Fabric Data Agent for backend service? (y/N): " config_foundry
  if [[ "$config_foundry" =~ ^[Yy] ]]; then
    prompt_var FOUNDRY_PROJECT_ENDPOINT          "Foundry project endpoint (https://<resource>.services.ai.azure.com/api/projects/<project>)"
    prompt_var FOUNDRY_AGENT_ID                  "Foundry Agent ID (not name; as shown in the AI Foundry portal agent details)"

    read -rp "  Foundry resource ID for RBAC (leave blank to skip): " foundry_rid
    if [[ -n "$foundry_rid" ]]; then
      EXISTING_FOUNDRY_RESOURCE_ID="$foundry_rid"
      if grep -q "^EXISTING_FOUNDRY_RESOURCE_ID=" "$ENV_FILE" 2>/dev/null; then
        sed -i "s|^EXISTING_FOUNDRY_RESOURCE_ID=.*|EXISTING_FOUNDRY_RESOURCE_ID=\"${foundry_rid}\"|" "$ENV_FILE"
      else
        echo "EXISTING_FOUNDRY_RESOURCE_ID=\"${foundry_rid}\"" >> "$ENV_FILE"
      fi
    fi
  fi
fi

prompt_var OPENAI_DEPLOYMENT_NAME "OpenAI realtime deployment name" "gpt-realtime"
prompt_var OPENAI_VOICE           "OpenAI voice (alloy/echo/fable/onyx/nova/shimmer)" "alloy"

# ─── Derived names ──────────────────────────────────────────────────────────
# Use a short hash for unique but deterministic resource names
HASH=$(echo -n "${ENVIRONMENT_NAME}-${RESOURCE_GROUP}" | sha256sum | head -c 10)
KV_NAME="kv-${HASH}"
MI_NAME="id-${HASH}"
MI_BACKEND_NAME="id-backend-${HASH}"
ACR_NAME="acr${HASH}"
LOG_NAME="log-${HASH}"
CAE_NAME="cae-${HASH}"
CA_VOICE_NAME="ca-voice-${HASH}"
CA_BACKEND_NAME="ca-backend-${HASH}"
ACS_NAME="acs-${HASH}"
OAI_NAME="oai-${HASH}"

echo "============================================================"
echo "Fabric Data Service + Voice Call Agent – Deployment"
echo "============================================================"
echo "Environment:    ${ENVIRONMENT_NAME}"
echo "Resource Group: ${RESOURCE_GROUP}"
echo "Location:       ${LOCATION}"
echo "Resource Hash:  ${HASH}"
echo ""

# ─── 1. Resource Group ──────────────────────────────────────────────────────
echo "▶ Creating Resource Group: ${RESOURCE_GROUP}"
az group create \
  --name "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --output none

# ─── 2. User-Assigned Managed Identities ────────────────────────────────────
echo "▶ Creating Managed Identity (Voice Agent): ${MI_NAME}"
az identity create \
  --name "$MI_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --output none

MI_CLIENT_ID=$(az identity show --name "$MI_NAME" --resource-group "$RESOURCE_GROUP" --query clientId -o tsv)
MI_PRINCIPAL_ID=$(az identity show --name "$MI_NAME" --resource-group "$RESOURCE_GROUP" --query principalId -o tsv)
MI_RESOURCE_ID=$(az identity show --name "$MI_NAME" --resource-group "$RESOURCE_GROUP" --query id -o tsv)
echo "  Voice MI Client ID:    ${MI_CLIENT_ID}"

echo "▶ Creating Managed Identity (Backend): ${MI_BACKEND_NAME}"
az identity create \
  --name "$MI_BACKEND_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --output none

MI_BACKEND_CLIENT_ID=$(az identity show --name "$MI_BACKEND_NAME" --resource-group "$RESOURCE_GROUP" --query clientId -o tsv)
MI_BACKEND_PRINCIPAL_ID=$(az identity show --name "$MI_BACKEND_NAME" --resource-group "$RESOURCE_GROUP" --query principalId -o tsv)
MI_BACKEND_RESOURCE_ID=$(az identity show --name "$MI_BACKEND_NAME" --resource-group "$RESOURCE_GROUP" --query id -o tsv)
echo "  Backend MI Client ID:  ${MI_BACKEND_CLIENT_ID}"

# ─── 3. Key Vault ───────────────────────────────────────────────────────────
echo "▶ Creating Key Vault: ${KV_NAME}"
az keyvault create \
  --name "$KV_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --enable-rbac-authorization true \
  --retention-days 7 \
  --output none

KV_ID=$(az keyvault show --name "$KV_NAME" --resource-group "$RESOURCE_GROUP" --query id -o tsv)

# Grant Key Vault Secrets User to the managed identity
echo "  Granting Key Vault Secrets User to managed identity..."
az role assignment create \
  --assignee-object-id "$MI_PRINCIPAL_ID" \
  --assignee-principal-type ServicePrincipal \
  --role "Key Vault Secrets User" \
  --scope "$KV_ID" \
  --output none 2>/dev/null || true

# Grant Key Vault Secrets Officer to current user (so we can write secrets)
CURRENT_USER_ID=$(az ad signed-in-user show --query id -o tsv)
az role assignment create \
  --assignee-object-id "$CURRENT_USER_ID" \
  --assignee-principal-type User \
  --role "Key Vault Secrets Officer" \
  --scope "$KV_ID" \
  --output none 2>/dev/null || true

# Wait for RBAC propagation
echo "  Waiting for RBAC propagation (30s)..."
sleep 30

# ─── 4. Azure Communication Services ────────────────────────────────────────
if [[ -n "${EXISTING_ACS_CONNECTION_STRING:-}" ]]; then
  echo "▶ Reusing existing ACS (connection string provided)"
  ACS_CONN_STRING="$EXISTING_ACS_CONNECTION_STRING"
else
  echo "▶ Creating Azure Communication Services: ${ACS_NAME}"
  az communication create \
    --name "$ACS_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --location global \
    --data-location "United States" \
    --output none

  ACS_CONN_STRING=$(az communication list-key \
    --name "$ACS_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --query primaryConnectionString -o tsv)
fi

# Store ACS connection string in Key Vault
echo "  Storing ACS connection string in Key Vault..."
az keyvault secret set \
  --vault-name "$KV_NAME" \
  --name "acs-connection-string" \
  --value "$ACS_CONN_STRING" \
  --output none

ACS_SECRET_URI=$(az keyvault secret show \
  --vault-name "$KV_NAME" \
  --name "acs-connection-string" \
  --query id -o tsv)

# ─── 5. Azure OpenAI ────────────────────────────────────────────────────────
if [[ -n "${EXISTING_OPENAI_ENDPOINT:-}" ]]; then
  echo "▶ Reusing existing Azure OpenAI"
  # Strip any trailing path (e.g. /openai/v1) – the app builds the full URL
  OAI_ENDPOINT=$(echo "$EXISTING_OPENAI_ENDPOINT" | sed 's|/openai.*||')
  OAI_API_KEY="${EXISTING_OPENAI_API_KEY:-}"

  if [[ -z "$OAI_API_KEY" ]]; then
    echo "  WARNING: No API key provided for existing OpenAI resource."
    echo "  The managed identity must have 'Cognitive Services OpenAI User' on that resource."
  fi

  # Grant managed identity access to existing OpenAI resource (if scope provided)
  if [[ -n "${EXISTING_OPENAI_RESOURCE_ID:-}" ]]; then
    echo "  Granting Cognitive Services OpenAI User to managed identity..."
    az role assignment create \
      --assignee-object-id "$MI_PRINCIPAL_ID" \
      --assignee-principal-type ServicePrincipal \
      --role "Cognitive Services OpenAI User" \
      --scope "$EXISTING_OPENAI_RESOURCE_ID" \
      --output none 2>/dev/null || true
  fi
else
  echo "▶ Creating Azure OpenAI: ${OAI_NAME}"
  az cognitiveservices account create \
    --name "$OAI_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --location "$LOCATION" \
    --kind OpenAI \
    --sku S0 \
    --custom-domain "$OAI_NAME" \
    --output none

  OAI_ENDPOINT=$(az cognitiveservices account show \
    --name "$OAI_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --query properties.endpoint -o tsv)

  OAI_API_KEY=$(az cognitiveservices account keys list \
    --name "$OAI_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --query key1 -o tsv)

  echo "  Deploying gpt-4o-realtime model..."
  az cognitiveservices account deployment create \
    --name "$OAI_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --deployment-name "${OPENAI_DEPLOYMENT_NAME:-gpt-realtime}" \
    --model-name "gpt-realtime-1.5" \
    --model-version "2026-02-23" \
    --model-format OpenAI \
    --sku-name GlobalStandard \
    --sku-capacity 10 \
    --output none

  OAI_RESOURCE_ID=$(az cognitiveservices account show \
    --name "$OAI_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --query id -o tsv)

  # Grant managed identity Cognitive Services OpenAI User on the new resource
  echo "  Granting Cognitive Services OpenAI User to managed identity..."
  az role assignment create \
    --assignee-object-id "$MI_PRINCIPAL_ID" \
    --assignee-principal-type ServicePrincipal \
    --role "Cognitive Services OpenAI User" \
    --scope "$OAI_RESOURCE_ID" \
    --output none 2>/dev/null || true
fi

# Store OpenAI API key in Key Vault
if [[ -n "${OAI_API_KEY:-}" ]]; then
  echo "  Storing OpenAI API key in Key Vault..."
  az keyvault secret set \
    --vault-name "$KV_NAME" \
    --name "openai-api-key" \
    --value "$OAI_API_KEY" \
    --output none

  OAI_SECRET_URI=$(az keyvault secret show \
    --vault-name "$KV_NAME" \
    --name "openai-api-key" \
    --query id -o tsv)
fi

# Strip trailing slash from endpoint for consistency
OAI_ENDPOINT="${OAI_ENDPOINT%/}"
echo "  OpenAI Endpoint: ${OAI_ENDPOINT}"

# ─── 6. Container Registry ──────────────────────────────────────────────────
echo "▶ Creating Container Registry: ${ACR_NAME}"
az acr create \
  --name "$ACR_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --sku Basic \
  --admin-enabled true \
  --output none

ACR_LOGIN_SERVER=$(az acr show --name "$ACR_NAME" --resource-group "$RESOURCE_GROUP" --query loginServer -o tsv)
ACR_USERNAME="$ACR_NAME"
ACR_PASSWORD=$(az acr credential show --name "$ACR_NAME" --query "passwords[0].value" -o tsv)

# ─── 7. Build & Push Container Images ───────────────────────────────────────
IMAGE_VOICE="${ACR_LOGIN_SERVER}/fabricvoicecallagent:latest"
IMAGE_BACKEND="${ACR_LOGIN_SERVER}/fabricdataservice:latest"

echo "▶ Building container image: ${IMAGE_BACKEND}"
az acr build \
  --registry "$ACR_NAME" \
  --image "fabricdataservice:latest" \
  --file "${SCRIPT_DIR}/src/FabricDataService/Dockerfile" \
  "${SCRIPT_DIR}/src/FabricDataService/" \
  --output none

echo "▶ Building container image: ${IMAGE_VOICE}"
az acr build \
  --registry "$ACR_NAME" \
  --image "fabricvoicecallagent:latest" \
  --file "${SCRIPT_DIR}/src/FabricVoiceCallAgent/Dockerfile" \
  "${SCRIPT_DIR}/src/FabricVoiceCallAgent/" \
  --output none

# ─── 8. Log Analytics & Container Apps Environment ──────────────────────────
echo "▶ Creating Log Analytics Workspace: ${LOG_NAME}"
az monitor log-analytics workspace create \
  --workspace-name "$LOG_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --retention-time 30 \
  --output none

LOG_CUSTOMER_ID=$(az monitor log-analytics workspace show \
  --workspace-name "$LOG_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --query customerId -o tsv)

LOG_SHARED_KEY=$(az monitor log-analytics workspace get-shared-keys \
  --workspace-name "$LOG_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --query primarySharedKey -o tsv)

echo "▶ Creating Container Apps Environment: ${CAE_NAME}"
az containerapp env create \
  --name "$CAE_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --logs-workspace-id "$LOG_CUSTOMER_ID" \
  --logs-workspace-key "$LOG_SHARED_KEY" \
  --output none

CAE_DOMAIN=$(az containerapp env show \
  --name "$CAE_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --query properties.defaultDomain -o tsv)

# ─── 9. FabricDataService Container App (internal ingress) ──────────────────
echo "▶ Creating Container App (Backend): ${CA_BACKEND_NAME}"

BACKEND_ENV_VARS=(
  "FabricDataService__ProjectEndpoint=${FOUNDRY_PROJECT_ENDPOINT:-}"
  "FabricDataService__AgentId=${FOUNDRY_AGENT_ID:-}"
  "FabricDataService__ManagedIdentityClientId=${MI_BACKEND_CLIENT_ID}"
)

az containerapp create \
  --name "$CA_BACKEND_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --environment "$CAE_NAME" \
  --image "$IMAGE_BACKEND" \
  --registry-server "$ACR_LOGIN_SERVER" \
  --registry-username "$ACR_USERNAME" \
  --registry-password "$ACR_PASSWORD" \
  --user-assigned "$MI_BACKEND_RESOURCE_ID" \
  --target-port 8080 \
  --ingress internal \
  --transport auto \
  --cpu 0.5 \
  --memory 1Gi \
  --min-replicas 1 \
  --max-replicas 3 \
  --env-vars "${BACKEND_ENV_VARS[@]}" \
  --output none

BACKEND_FQDN=$(az containerapp show \
  --name "$CA_BACKEND_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --query properties.configuration.ingress.fqdn -o tsv)
BACKEND_URL="https://${BACKEND_FQDN}"
echo "  Backend URL (internal): ${BACKEND_URL}"

# ─── 10. FabricVoiceCallAgent Container App (external ingress) ──────────────
CALLBACK_BASE_URL="https://${CA_VOICE_NAME}.${CAE_DOMAIN}"

echo "▶ Creating Container App (Voice Agent): ${CA_VOICE_NAME}"

# Build secrets array conditionally - always include ACS connection string
SECRETS_ARG="acs-connection-string=keyvaultref:${ACS_SECRET_URI},identityref:${MI_RESOURCE_ID}"

# Build env-vars array
VOICE_ENV_VARS=(
  "Acs__ConnectionString=secretref:acs-connection-string"
  "Acs__PhoneNumber=${ACS_PHONE_NUMBER}"
  "Acs__CallbackBaseUrl=${CALLBACK_BASE_URL}"
  "OpenAi__Endpoint=${OAI_ENDPOINT}"
  "OpenAi__DeploymentName=${OPENAI_DEPLOYMENT_NAME:-gpt-realtime}"
  "OpenAi__Voice=${OPENAI_VOICE:-alloy}"
  "FabricBackend__BaseUrl=${BACKEND_URL}"
  "FabricBackend__DefaultTimeoutSeconds=60"
  "FabricBackend__FollowUpTimeoutSeconds=20"
  "VoiceAgent__DefaultPhoneNumber=${DEFAULT_PHONE_NUMBER}"
  "VoiceAgent__ManagedIdentityClientId=${MI_CLIENT_ID}"
)

# Only add OpenAI API key secret/env-var if we have an API key stored in Key Vault
if [[ -n "${OAI_SECRET_URI:-}" ]]; then
  SECRETS_ARG="${SECRETS_ARG} openai-api-key=keyvaultref:${OAI_SECRET_URI},identityref:${MI_RESOURCE_ID}"
  VOICE_ENV_VARS+=("OpenAi__ApiKey=secretref:openai-api-key")
  echo "  Using OpenAI API key from Key Vault"
else
  echo "  No OpenAI API key configured - app will use managed identity authentication"
fi

az containerapp create \
  --name "$CA_VOICE_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --environment "$CAE_NAME" \
  --image "$IMAGE_VOICE" \
  --registry-server "$ACR_LOGIN_SERVER" \
  --registry-username "$ACR_USERNAME" \
  --registry-password "$ACR_PASSWORD" \
  --user-assigned "$MI_RESOURCE_ID" \
  --target-port 8080 \
  --ingress external \
  --transport auto \
  --cpu 0.5 \
  --memory 1Gi \
  --min-replicas 1 \
  --max-replicas 1 \
  --secrets $SECRETS_ARG \
  --env-vars "${VOICE_ENV_VARS[@]}" \
  --output none

# Get the final FQDN (may differ slightly from pre-computed)
CA_FQDN=$(az containerapp show \
  --name "$CA_VOICE_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --query properties.configuration.ingress.fqdn -o tsv)

# ─── 11. Grant Foundry RBAC to Backend MI ───────────────────────────────────
if [[ -n "${EXISTING_FOUNDRY_RESOURCE_ID:-}" ]]; then
  echo "▶ Granting backend managed identity access to AI Foundry project..."
  az role assignment create \
    --assignee-object-id "$MI_BACKEND_PRINCIPAL_ID" \
    --assignee-principal-type ServicePrincipal \
    --role "Azure AI User" \
    --scope "$EXISTING_FOUNDRY_RESOURCE_ID" \
    --output none 2>/dev/null || true
  az role assignment create \
    --assignee-object-id "$MI_BACKEND_PRINCIPAL_ID" \
    --assignee-principal-type ServicePrincipal \
    --role "Azure AI Developer" \
    --scope "$EXISTING_FOUNDRY_RESOURCE_ID" \
    --output none 2>/dev/null || true
fi

# ─── Done ────────────────────────────────────────────────────────────────────
echo ""
echo "============================================================"
echo "  Deployment Complete!"
echo "============================================================"
echo ""
echo "Voice Agent URL:       https://${CA_FQDN}"
echo "Alert Webhook:         https://${CA_FQDN}/api/alert"
echo "Callback URL:          https://${CA_FQDN}/api/callbacks"
echo "WebSocket URL:         wss://${CA_FQDN}/ws/audio"
echo ""
echo "Backend URL (internal): ${BACKEND_URL}"
echo "Backend Health:         ${BACKEND_URL}/health/live"
echo ""
echo "Voice MI:              ${MI_CLIENT_ID}"
echo "Backend MI:            ${MI_BACKEND_CLIENT_ID}"
echo "Key Vault:             ${KV_NAME}"
echo ""
echo "── Test with: ──"
echo "curl -X POST https://${CA_FQDN}/api/alert \\"
echo "  -H 'Content-Type: application/json' \\"
echo "  -d '{\"sourceId\":\"TEST-001\",\"sourceName\":\"Test\",\"alertType\":\"Threshold\",\"severity\":\"High\",\"title\":\"Test Alert\",\"description\":\"Testing voice agent\",\"metadata\":{}}'"
echo ""
