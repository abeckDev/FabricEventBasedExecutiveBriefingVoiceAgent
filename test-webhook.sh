#!/bin/bash

# ============================================================
# Test Script for Fabric Voice Call Agent
# ============================================================

# Update these values for your deployment
WEBHOOK_URL="https://<your-container-app>.azurecontainerapps.io/api/alert"
PHONE_NUMBER="+1234567890"

echo "📞 Fabric Voice Call Agent - Test Script"
echo "==========================================="
echo ""
echo "Webhook URL: $WEBHOOK_URL"
echo "Phone Number: $PHONE_NUMBER"
echo ""
echo "Sending test alert..."
echo ""

curl -X POST "$WEBHOOK_URL" \
  -H "Content-Type: application/json" \
  -d '{
    "sourceId": "ORD-B61C8729",
    "sourceName": "CUST-BRAVO",
    "alertType": "Threshold",
    "severity": "High",
    "title": "Order is jeopardized",
    "description": "A critical threshold has been exceeded and requires immediate attention.",
    "timestamp": "2024-01-15T14:30:00Z",
    "phoneNumber": "'"$PHONE_NUMBER"'",
    "metadata": {
      "metric1": 1.45,
      "metric2": 87.3,
      "referenceId": "REF-2024-001"
    }
  }' \
  -w "\n\nHTTP Status: %{http_code}\n" \
  -s

echo ""
echo "✅ Alert sent!"
echo ""
echo "Expected behavior:"
echo "1. The application receives the alert"
echo "2. Foundry Agent queries your Fabric Data Agent for context"
echo "3. Generates a ~30-second executive summary"
echo "4. Places a call to $PHONE_NUMBER"
echo "5. Speaks the summary via OpenAI Realtime API"
echo "6. Enables realtime voice Q&A for follow-up questions"
echo ""
echo "📱 You should receive a call in ~10-15 seconds!"
echo ""
echo "To check logs:"
echo "az containerapp logs show --name YOUR_CONTAINER_APP --resource-group YOUR_RESOURCE_GROUP --follow"
