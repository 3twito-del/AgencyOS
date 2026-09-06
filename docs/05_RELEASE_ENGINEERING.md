# Release Engineering

## Rings

FORGE -> LAB -> NIGHTLY -> ALPHA -> BETA -> RC -> STABLE

### FORGE
- upstream daily/HEAD/custom forks;
- synthetic/sanitized data only;
- destructive reset allowed;
- no compatibility promise.

### LAB
- official experimental/beta/RC technology;
- isolated DB and storage;
- capability experiments.

### NIGHTLY
- automatic build from integration branch;
- must compile and pass baseline tests;
- forced update to latest build.

### ALPHA
- primary private real-data channel during development;
- latest promoted toolchain;
- strict migration/backup gates;
- may break UX, must not casually endanger data.

### BETA
- feature-complete release candidate;
- compatibility window;
- production-like observability.

### RC
- no feature additions except critical fixes.

### STABLE
- signed;
- SBOM/provenance;
- staged rollout;
- tested backup/restore and rollback;
- organizational dependency permitted.

## Side-by-side package identities

- AgencyOS.Forge
- AgencyOS.Lab
- AgencyOS.Nightly
- AgencyOS.Alpha
- AgencyOS.Stable

Each must have isolated:
- package identity;
- local cache;
- settings;
- endpoint environment;
- telemetry environment.

FORGE/LAB must never point at the canonical real-data environment.
