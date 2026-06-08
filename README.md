# CveNet

Multi-agent CVE security analysis system built with ASP.NET Core Minimal APIs on Azure Kubernetes Service.

## Secret management

`k8s/config.yaml` and all `appsettings.json` files are **git-ignored** — they may contain real API keys.
Committed templates (`*.template.json`, `k8s/config.yaml.template`) contain the structure with empty values.

```bash
# First-time setup: copy templates, then fill in real values
cp k8s/config.yaml.template k8s/config.yaml
cp src/CveNet.Orchestrator/appsettings.template.json src/CveNet.Orchestrator/appsettings.json
# ... repeat for each service, then add real keys
```

## Architecture

```
User Query
    │
    ▼
CveNet.Orchestrator  (LoadBalancer :8080)
    │
    ├─── POST /parse  ──────► CveNet.PromptParser   (ClusterIP)
    │                              └── Azure OpenAI (gpt-35-turbo)
    │
    ├─── POST /search ──────► CveNet.CveSearch       (ClusterIP)
    │                              ├── Azure AI Search (RAG)
    │                              └── Azure OpenAI (LLM fallback)
    │
    ├─── POST /prioritize ──► CveNet.Prioritization  (ClusterIP)
    │                              └── Composite scoring + Azure OpenAI
    │
    └─── POST /report ──────► CveNet.Report          (ClusterIP)
                                   ├── Azure OpenAI (synthesis)
                                   └── Azure Blob Storage (optional)
```

## Prerequisites

- .NET 9 SDK
- Docker
- Azure CLI (`az`)
- `kubectl` + AKS cluster
- Azure resources: OpenAI (Free), AI Search (Free), Storage (optional)

## Azure Resource Setup

### 1. Azure OpenAI (Free Tier)

```bash
az cognitiveservices account create \
  --name cvenet-oai \
  --resource-group cvenet-rg \
  --kind OpenAI \
  --sku F0 \
  --location eastus

az cognitiveservices account deployment create \
  --name cvenet-oai \
  --resource-group cvenet-rg \
  --deployment-name gpt-35-turbo \
  --model-name gpt-35-turbo \
  --model-version "0125" \
  --model-format OpenAI \
  --sku-capacity 1 \
  --sku-name Standard

# Get endpoint and key
az cognitiveservices account show \
  --name cvenet-oai \
  --resource-group cvenet-rg \
  --query properties.endpoint -o tsv

az cognitiveservices account keys list \
  --name cvenet-oai \
  --resource-group cvenet-rg \
  --query key1 -o tsv
```

> **Free Tier Limit:** 1 request/min, 1,000 tokens/min, 10,000 tokens/day on gpt-35-turbo.
> Each pipeline run makes ~4 LLM calls. Budget ~5 analyses/day on free tier.

### 2. Azure AI Search (Free Tier)

```bash
az search service create \
  --name cvenet-search \
  --resource-group cvenet-rg \
  --sku Free \
  --location eastus

# Get endpoint (https://<name>.search.windows.net)
az search service show \
  --name cvenet-search \
  --resource-group cvenet-rg \
  --query properties.hostName -o tsv

# Get admin key
az search admin-key show \
  --service-name cvenet-search \
  --resource-group cvenet-rg \
  --query primaryKey -o tsv
```

> **Free Tier Limit:** 1 index, 50MB storage, 10,000 documents, 3 replicas max.

#### Create the cvenet-index

```bash
# Create index with required fields
curl -X PUT "https://cvenet-search.search.windows.net/indexes/cvenet-index?api-version=2023-11-01" \
  -H "Content-Type: application/json" \
  -H "api-key: <SEARCH_ADMIN_KEY>" \
  -d '{
    "name": "cvenet-index",
    "fields": [
      {"name": "id",               "type": "Edm.String", "key": true, "searchable": false},
      {"name": "cveId",            "type": "Edm.String", "searchable": true, "filterable": true},
      {"name": "description",      "type": "Edm.String", "searchable": true},
      {"name": "cvssScore",        "type": "Edm.Double", "filterable": true, "sortable": true},
      {"name": "severity",         "type": "Edm.String", "filterable": true, "facetable": true},
      {"name": "affectedProducts", "type": "Collection(Edm.String)", "searchable": true},
      {"name": "publishedDate",    "type": "Edm.DateTimeOffset", "filterable": true, "sortable": true},
      {"name": "references",       "type": "Edm.String", "searchable": false},
      {"name": "remediation",      "type": "Edm.String", "searchable": true}
    ]
  }'
```

### 3. Azure Blob Storage (Optional)

```bash
az storage account create \
  --name cvenetstorage \
  --resource-group cvenet-rg \
  --sku Standard_LRS \
  --kind StorageV2

az storage container create \
  --name cvenet-reports \
  --account-name cvenetstorage

# Get connection string
az storage account show-connection-string \
  --name cvenetstorage \
  --resource-group cvenet-rg \
  --query connectionString -o tsv
```

### 4. AKS Cluster

```bash
az aks create \
  --name cvenet-aks \
  --resource-group cvenet-rg \
  --node-count 2 \
  --node-vm-size Standard_B2s \
  --generate-ssh-keys

az aks get-credentials \
  --name cvenet-aks \
  --resource-group cvenet-rg
```

## Build & Push Images

```bash
ACR_NAME=<your-acr-name>

az acr login --name $ACR_NAME

# Build and push each service
for SERVICE in Orchestrator PromptParser CveSearch Prioritization Report; do
  IMAGE_NAME=$(echo $SERVICE | tr '[:upper:]' '[:lower:]')
  docker build \
    --build-arg PROJECT=CveNet.$SERVICE \
    -t $ACR_NAME.azurecr.io/cvenet-$IMAGE_NAME:latest \
    .
  docker push $ACR_NAME.azurecr.io/cvenet-$IMAGE_NAME:latest
done
```

## Kubernetes Deployment

### 1. Populate Secrets

```bash
# Base64-encode each value
OAI_ENDPOINT=$(echo -n "https://cvenet-oai.openai.azure.com/" | base64)
OAI_KEY=$(echo -n "<your-oai-key>" | base64)
SEARCH_ENDPOINT=$(echo -n "https://cvenet-search.search.windows.net" | base64)
SEARCH_KEY=$(echo -n "<your-search-key>" | base64)
BLOB_CONN=$(echo -n "<your-storage-connection-string>" | base64)

# Edit k8s/config.yaml and paste the base64 values into the Secret data section
```

### 2. Apply manifests

```bash
# Namespace, Secret, ConfigMap
kubectl apply -f k8s/config.yaml

# Update <ACR_NAME> in deployments.yaml, then:
sed -i "s/<ACR_NAME>/$ACR_NAME/g" k8s/deployments.yaml
kubectl apply -f k8s/deployments.yaml

# Verify all pods are running
kubectl get pods -n cvenet
kubectl get svc -n cvenet
```

### 3. Test the API

```bash
# Get orchestrator external IP
EXTERNAL_IP=$(kubectl get svc cvenet-orchestrator-svc -n cvenet -o jsonpath='{.status.loadBalancer.ingress[0].ip}')

# Example query
curl -X POST http://$EXTERNAL_IP:8080/analyze \
  -H "Content-Type: application/json" \
  -d '{
    "text": "What are the critical CVEs affecting Apache Log4j in the last 90 days?",
    "sessionId": "test-001"
  }'
```

## Local Development

```bash
# Run individual services locally (requires appsettings filled in)
cd src/CveNet.Orchestrator && dotnet run

# Or set env vars inline
AzureOpenAI__Endpoint="https://..." AzureOpenAI__ApiKey="..." dotnet run
```

Override agent URLs for local testing in `appsettings.Development.json`:

```json
{
  "Agents": {
    "PromptParser":   "http://localhost:5001",
    "CveSearch":      "http://localhost:5002",
    "Prioritization": "http://localhost:5003",
    "Report":         "http://localhost:5004"
  }
}
```

## Free Tier Limits Summary

| Service            | Limit                                                   |
|--------------------|---------------------------------------------------------|
| Azure OpenAI F0    | 1 req/min, 1K tokens/min, 10K tokens/day                |
| Azure AI Search F  | 1 index, 50MB, 10K docs, 3 units max                    |
| AKS (B2s × 2)      | ~$140/month (no free tier; use `az aks stop` when idle) |
| Blob Storage       | First 5GB free/month (LRS)                              |

> **Tip:** Run `az aks stop --name cvenet-aks --resource-group cvenet-rg` when not in use to pause the AKS billing clock.

## Project Structure

```
CveNet/
├── CveNet.sln
├── Dockerfile                    # Multi-stage, --build-arg PROJECT=
├── k8s/
│   ├── config.yaml               # Namespace, Secret, ConfigMap
│   └── deployments.yaml          # Deployments, Services, HPAs
└── src/
    ├── CveNet.Shared/            # Shared models (records)
    │   └── Models/
    ├── CveNet.Orchestrator/      # Entry point, fan-out pipeline
    │   └── Services/OrchestratorService.cs
    ├── CveNet.PromptParser/      # Intent classification via LLM
    │   └── Services/PromptParserService.cs
    ├── CveNet.CveSearch/         # RAG retrieval from Azure AI Search
    │   └── Services/CveSearchService.cs
    ├── CveNet.Prioritization/    # Composite risk scoring
    │   └── Services/PrioritizationService.cs
    └── CveNet.Report/            # Report synthesis + Blob persistence
        └── Services/ReportService.cs
```
