# Changelog

All notable changes to this project are documented here, newest first.
Format loosely follows [Keep a Changelog](https://keepachangelog.com/).

## 2026-06-09

### Security
- Removed `k8s/config.yaml` and all `src/**/appsettings.json` from git tracking — they're git-ignored and must be generated locally from `*.template` files.
- Parameterized `deploy.sh` to read Azure OpenAI / Search / Storage endpoints and keys from environment variables instead of hardcoded values.
- Replaced hardcoded ACR login server URL in `k8s/deployments.yaml` with `<ACR_NAME>.azurecr.io` placeholders.

### Added
- Redesigned `docs/pipeline.svg` — vertical AKS-layout architecture diagram with animated request flow.
- Added `docs/architecture-demo.mov` as the primary architecture visual in the README, with the static SVG moved to a collapsible "Static diagram" section.

### Changed
- Updated README clone URL to `github.com/Jagadish-builds/CveNet`.
- Updated README to reflect the current pipeline: Azure OpenAI deployment is `gpt-4.1-mini`, Prioritization is fully deterministic (CVSS + severity + recency, no LLM call), and Report generates its summary and recommendations concurrently via `Task.WhenAll`.

## Earlier

- Reduced pipeline latency: removed the Prioritization LLM call, parallelized the two Report LLM calls.
- Runtime fixes, package corrections, and search schema alignment.
- Reduced CPU requests to fit a single AKS node.
- Pre-public hardening: security, validation, and licensing pass; added `.gitignore` and config templates; rewrote README for a public audience; added Mermaid architecture diagram and AI features table.
