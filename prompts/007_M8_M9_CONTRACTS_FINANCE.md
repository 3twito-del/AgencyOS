# Prompt 007 — M8/M9 Contracts, Rights & Finance

Treat this as high-integrity work.

Contracts/Rights:
- effective dates;
- options;
- expirations;
- obligations;
- document versions;
- immutable historical trace.

Finance:
- never binary float;
- amount + currency;
- invoice/payment/commission;
- allocation;
- immutable journal;
- double-entry ledger design before posting logic.

Before coding ledger posting, write:
1. module specification;
2. invariants;
3. property-based test plan;
4. TLA+/formal model if concurrency/idempotency can violate accounting truth.
