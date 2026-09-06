# Product Specification

## 1. Core entity families

### Parties
- Person
- Organization
- Team
- Office
- User
- Membership

### Assets
- Talent Profile
- Project
- IP
- Material
- Role
- Credit
- Brand/Property

### Relationships
- ProfessionalRelationship
- Representation
- Employment/Affiliation
- Introduction
- Attachment
- Collaboration
- Ownership
- RightsRelationship

### Transactions
- Opportunity
- Submission
- Pitch
- Meeting
- Offer
- CounterOffer
- Deal
- Contract
- Invoice
- Payment
- Commission
- LedgerEntry

### Activity
- Interaction
- Note
- Task
- Reminder
- Event
- Change/AuditEvent

### Intelligence
- Signal
- Source
- Thesis
- Prediction
- Watchlist
- Risk
- OpportunityInsight

## 2. Core user experience

### Command Center
Must answer:
- What changed?
- What requires attention?
- Which relationships need action?
- Which deals are moving/stuck?
- Which clients/projects are exposed to risk?
- What money is due/overdue?
- What is the highest-leverage next move?

### Universal Search / Command Palette
Support:
- exact entity lookup;
- structured queries;
- saved views;
- domain commands;
- natural-language interpretation through AI tools;
- keyboard-first use.

### Unified Timeline
Every core entity can surface a chronological activity view linking:
- interactions;
- status changes;
- documents;
- submissions;
- offers/counters;
- tasks;
- payments;
- audit events.

## 3. AI principles

AI may:
- read authorized data;
- summarize;
- compare;
- extract;
- rank;
- propose;
- create reversible records when authorized.

AI may not:
- bypass permissions;
- directly modify canonical DB state;
- execute sensitive legal/financial actions without required approval;
- obscure source provenance.

## 4. Non-goals for initial versions

Do not build:
- public marketplace;
- consumer social network;
- generic CRM SaaS;
- mobile clients before Windows/API stabilizes;
- autonomous outbound-spam system.
