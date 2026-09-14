# AgencyOS Review Harness

Engineering tooling. Not product, and not a dependency of it.

The harness gathers evidence about AgencyOS as a running product: what surfaces
exist, which of them anything can reach, what the running window exposes to
Windows UI Automation, and what it looks like. It draws no conclusions. A person
reads the evidence and writes the findings.

## Direction of dependency

The reviewer references `AgencyOS.Client`, `AgencyOS.Windows.Platform` and
`AgencyOS.Contracts`, one way only. No product project references the reviewer,
and `ReviewerBoundaryTests` asserts both halves of that by reading the project
files. A review tool that becomes a dependency of the thing it reviews stops
being independent.

## Modes

| Mode | What it does | Needs |
| --- | --- | --- |
| `inventory` | Surface map, reachability graph, XAML style inventory | Source only. Deterministic, CI-safe. |
| `fixture` | Builds a synthetic review tenant through `AgencyOsApiClient` | A running API on a ring that forbids real data |
| `observe` | Launches the WinUI client and drives it through UI Automation | A real interactive desktop session |
| `report` | Renders the summary statistics and HTML report from the ledger | The findings ledger |
| `reproduce` | Prints one finding's record from the ledger | The findings ledger |
| `repair` | **Refuses.** Exit code 3. | — |

```sh
dotnet run --project tools/AgencyOS.Reviewer -- inventory --repo . --out artifacts/reviewer/static
dotnet run --project tools/AgencyOS.Reviewer -- fixture  --api http://127.0.0.1:5199 --token <token>
dotnet run --project tools/AgencyOS.Reviewer -- observe  --exe <client.exe> --out <run-dir> --org <guid>
dotnet run --project tools/AgencyOS.Reviewer -- report   --out <run-dir>
```

## What belongs in CI, and what does not

`inventory` and everything in `AgencyOS.Tests.Reviewer` are deterministic: they
read source, need no desktop, no database and no network. They belong in CI.

`observe` needs a real interactive desktop. Its screenshots and automation trees
are **LAB evidence**, not a CI gate. A headless substitute would produce
artefacts that look like visual QA and are not, so the harness does not offer
one.

## Safety properties

These are deliberate, and the code says so where it enforces them:

- **Screen capture is scoped to one window handle**, never the desktop, so a
  screenshot cannot contain whatever else is open on the machine.
- **Every synthetic keystroke checks that the foreground window still belongs to
  the process under review** and is refused otherwise, so stolen focus cannot
  turn a keyboard audit into typing into another application.
- **The key set is closed** — navigation, activation and the gestures AgencyOS
  itself declares. `PressGesture` refuses an unmodified letter, because that is
  typing rather than a gesture.
- **The fixture writes only through `AgencyOsApiClient`**, so it cannot
  manufacture domain state the product would refuse, and never writes to
  PostgreSQL directly.
- **`repair` refuses.** Repair is a separate, explicitly approved wave against
  named finding identifiers; it is never reachable from an audit.

## Run artefacts

Everything a run produces goes under `artifacts/reviewer/`, which is ignored by
Git. Screenshots and automation trees are evidence for one run against one
synthetic tenant; committing them would put pictures of a database into the
history permanently.

## Known limitation

Run 001 found that the harness did not verify its own environment: it ran
`initdb` and `pg_ctl start` for a private cluster, the start failed because the
port was taken, and the harness carried on against whatever answered. Check the
data directory of the server you reach before trusting an isolation claim:

```sh
psql -h 127.0.0.1 -p <port> -U postgres -c "show data_directory;"
```
