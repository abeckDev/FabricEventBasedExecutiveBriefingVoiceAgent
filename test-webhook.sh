#!/bin/bash

# ============================================================
# Test Script for Smart Factory Voice Agent
# ============================================================

WEBHOOK_URL="https://<your-container-app-url>/api/alert"
MANAGER_PHONE="+1234567890"

echo "🏭 Smart Factory Voice Agent - Test Script"
echo "==========================================="
echo ""
echo "Webhook URL: $WEBHOOK_URL"
echo "Manager Phone: $MANAGER_PHONE"
echo ""
echo "Sending vibration anomaly alert..."
echo ""

curl -X POST "$WEBHOOK_URL" \
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
  }' \
  -w "\n\nHTTP Status: %{http_code}\n" \
  -s

echo ""
echo "✅ Alert sent!"
echo ""
echo "Expected behavior:"
echo "1. The application receives the alert"
echo "2. Foundry Agent queries your Eventhouse for context"
echo "3. Generates a ~30-second executive summary"
echo "4. Places a call to $MANAGER_PHONE"
echo "5. Plays the summary via TTS"
echo "6. Enables realtime voice Q&A with OpenAI Realtime API"
echo ""
echo "📱 You should receive a call in ~10-15 seconds!"
echo ""
echo "To check logs:"
echo "az containerapp logs show --name <your-container-app-name> --resource-group <your-resource-group> --follow"