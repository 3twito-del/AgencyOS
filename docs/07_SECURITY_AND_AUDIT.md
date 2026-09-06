# Security & Audit

## Identity
Early:
- development identity + explicit authorization architecture.

Mature:
- OpenID Connect / OAuth 2.x;
- Microsoft Entra ID where appropriate;
- Windows Hello / passkeys;
- MFA/conditional access.

## Authorization
Use:
- RBAC for broad roles;
- policy/attribute-based authorization for context;
- relationship-aware checks where needed.

Never trust the Windows client to enforce permissions.

## Data classification
Suggested:
- PUBLIC
- INTERNAL
- CONFIDENTIAL
- PRIVILEGED
- FINANCIAL
- RESTRICTED

## Audit
Consequential changes create immutable audit records:
- actor;
- source/client;
- timestamp;
- entity;
- action;
- before/after or semantic delta;
- reason when required;
- correlation/trace id.

## Secrets
- no secrets in source control;
- .NET User Secrets/environment in development;
- vault/HSM-backed secrets in mature deployment.

## Supply chain
Stable builds should include:
- dependency lockfiles;
- SBOM;
- artifact hashes;
- build provenance;
- vulnerability scanning;
- secret scanning;
- code signing;
- MSIX signing.
