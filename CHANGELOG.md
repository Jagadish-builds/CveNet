# Changelog

All notable changes to this project are documented here, newest first.

| Date | Type | Change |
|---|---|---|
| 2026-06-09 | Security | Removed `k8s/config.yaml` and all `src/**/appsettings.json` from git tracking — git-ignored, generate locally from `*.template` files |
| 2026-06-09 | Security | Parameterized `deploy.sh` to read Azure OpenAI / Search / Storage endpoints and keys from environment variables |
| 2026-06-09 | Security | Replaced hardcoded ACR login server URL in `k8s/deployments.yaml` with `<ACR_NAME>.azurecr.io` placeholders |
| 2026-06-09 | Added | Redesigned `docs/pipeline.svg` — vertical AKS-layout architecture diagram with animated request flow |
| 2026-06-09 | Added | Added `docs/architecture-demo.mov` as the primary architecture visual in the README; static SVG moved to a collapsible section |
| 2026-06-09 | Changed | Updated README clone URL to `github.com/Jagadish-builds/CveNet` |
| 2026-06-09 | Changed | Updated README to reflect `gpt-4.1-mini`, deterministic Prioritization (no LLM call), and Report's parallel `Task.WhenAll` synthesis |
| 2026-06-08 | Performance | Removed the Prioritization LLM call; parallelized the two Report LLM calls |
| 2026-06-08 | Fix | Runtime fixes, package corrections, and search schema alignment |
| 2026-06-08 | Fix | Reduced CPU requests to fit a single AKS node |
| 2026-06-08 | Security | Pre-public hardening pass: security, validation, and licensing |
| 2026-06-08 | Added | `.gitignore` and config templates, Mermaid architecture diagram, AI features table |
| 2026-06-08 | Docs | Rewrote README for a public audience |
