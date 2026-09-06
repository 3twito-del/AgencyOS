# Prompt 006 — M7 Deal Engine

Before implementation:
- define Offer, CounterOffer, Negotiation, Deal, DealTerm and transition invariants;
- compare C# vs F# for the state-machine/rules core;
- if F# materially improves illegal-state prevention, isolate the F# rules library behind a clean .NET contract;
- write ADR documenting the choice.

Implement command-oriented transitions, not arbitrary status PATCHing.

Examples:
- ReceiveOffer
- CounterOffer
- WithdrawOffer
- AcceptOffer
- CloseDeal
- AbandonNegotiation

Add property-based tests for transition legality.
