#!/usr/bin/env bash
set -euo pipefail

# ── Guard: require env vars ───────────────────────────────────────────────────
if [[ -z "${AZURE_OPENAI_ENDPOINT:-}" ]]; then
  echo "ERROR: AZURE_OPENAI_ENDPOINT is not set (e.g. https://<your-resource>.openai.azure.com/)" >&2; exit 1
fi
if [[ -z "${AZURE_OPENAI_KEY:-}" ]]; then
  echo "ERROR: AZURE_OPENAI_KEY is not set" >&2; exit 1
fi
if [[ -z "${AZURE_SEARCH_ENDPOINT:-}" ]]; then
  echo "ERROR: AZURE_SEARCH_ENDPOINT is not set (e.g. https://<your-resource>.search.windows.net)" >&2; exit 1
fi
if [[ -z "${AZURE_SEARCH_KEY:-}" ]]; then
  echo "ERROR: AZURE_SEARCH_KEY is not set" >&2; exit 1
fi

# ── Base64-encode secret values ───────────────────────────────────────────────
B64_OPENAI_ENDPOINT=$(echo -n "${AZURE_OPENAI_ENDPOINT}" | base64)
B64_OPENAI_KEY=$(echo -n "${AZURE_OPENAI_KEY}" | base64)
B64_SEARCH_ENDPOINT=$(echo -n "${AZURE_SEARCH_ENDPOINT}" | base64)
B64_SEARCH_KEY=$(echo -n "${AZURE_SEARCH_KEY}" | base64)
B64_STORAGE_CONN=$(echo -n "${AZURE_STORAGE_CONN:-}" | base64)

# ── Write populated config.yaml ───────────────────────────────────────────────
cat > k8s/config.yaml <<EOF
apiVersion: v1
kind: Namespace
metadata:
  name: cvenet

---
apiVersion: v1
kind: Secret
metadata:
  name: cvenet-secrets
  namespace: cvenet
type: Opaque
data:
  AZUREOPENAI__ENDPOINT: "${B64_OPENAI_ENDPOINT}"
  AZUREOPENAI__APIKEY: "${B64_OPENAI_KEY}"
  AZURESEARCH__ENDPOINT: "${B64_SEARCH_ENDPOINT}"
  AZURESEARCH__APIKEY: "${B64_SEARCH_KEY}"
  AZURESTORAGE__CONNECTIONSTRING: "${B64_STORAGE_CONN}"

---
apiVersion: v1
kind: ConfigMap
metadata:
  name: cvenet-config
  namespace: cvenet
data:
  AZUREOPENAI__DEPLOYMENT: "gpt-4.1-mini"
  AZURESEARCH__INDEXNAME: "cvenet-index"
  ASPNETCORE_ENVIRONMENT: "Production"
  ASPNETCORE_URLS: "http://+:8080"
  AGENTS__PROMPTPARSER: "http://cvenet-promptparser-svc:8080"
  AGENTS__CVESEARCH: "http://cvenet-cvesearch-svc:8080"
  AGENTS__PRIORITIZATION: "http://cvenet-prioritization-svc:8080"
  AGENTS__REPORT: "http://cvenet-report-svc:8080"
EOF

echo "✓ k8s/config.yaml written"

# ── Step 2: Create namespace (ignore if exists) ───────────────────────────────
kubectl create namespace cvenet --dry-run=client -o yaml | kubectl apply -f -
echo "✓ namespace cvenet ready"

# ── Step 3: Apply secrets + configmap ────────────────────────────────────────
kubectl apply -f k8s/config.yaml -n cvenet
echo "✓ secrets and configmap applied"

# ── Step 4: Apply deployments ─────────────────────────────────────────────────
kubectl apply -f k8s/deployments.yaml -n cvenet
echo "✓ deployments applied"

# ── Step 5: Initial pod status ────────────────────────────────────────────────
echo ""
echo "=== Pod status ==="
kubectl get pods -n cvenet

# ── Step 6: Watch until all pods are Ready ────────────────────────────────────
echo ""
echo "=== Watching pods (Ctrl+C when all Running) ==="
kubectl get pods -n cvenet --watch
