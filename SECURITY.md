# Security Policy

## Supported versions

| Version | Supported |
|---|---|
| `main` branch | Yes |

## Reporting a vulnerability

**Please do not open a public GitHub issue for security vulnerabilities.**

Report them privately via GitHub's built-in security advisory feature:

1. Go to the [Security tab](../../security) of this repository
2. Click **"Report a vulnerability"**
3. Describe the issue, steps to reproduce, and potential impact

You can also email the maintainer directly. We aim to acknowledge reports within **48 hours** and provide a fix or mitigation plan within **7 days** for confirmed issues.

## Scope

The following are in scope:

- Prompt injection via the `/analyze` endpoint
- Secrets leaking through logs or API responses
- Authentication/authorisation bypasses (if auth is added)
- Dependency vulnerabilities in NuGet packages

## Out of scope

- Vulnerabilities in Azure-managed services (OpenAI, AI Search, AKS) — report these to Microsoft
- Denial-of-service against the Azure free-tier quota (documented limitation)
- Issues only reproducible with misconfigured deployment

## Security notes for deployers

- Never commit real values to `appsettings.json` or `k8s/config.yaml` — these are git-ignored by design
- Rotate Azure OpenAI and AI Search keys if you suspect they were exposed
- The Orchestrator's `/analyze` endpoint has no authentication by default — add an API gateway or Azure API Management before exposing it publicly
