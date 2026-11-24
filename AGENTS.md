# AGENTS.md

Instructions for coding agents (Claude Code, Copilot, Codex and others) working in this repository. People are
welcome to read it too: it is a short map of the repo and the rules every change follows.

## Map

- `src/` is the `dcl` CLI (.NET 10). Module boundaries are in [ADR-0001](docs/adr/0001-module-boundaries.md):
  `DcLedger.Domain` ← `DcLedger.Application` ← {`DcLedger.Adapters.Legistar`, `DcLedger.Infrastructure`,
  `DcLedger.Publishing`} ← `DcLedger.Cli`. Domain uses the BCL only. `Cli` is the only composition root.
  `tests/DcLedger.Architecture.Tests` fails the build on a forbidden reference.
- `data/` holds the reviewed YAML records ([data/README.md](data/README.md)).
- `site/` is the Astro site. It reads only exported files under `out/site-data/`, never `data/`.
- `tools/` holds the Python scripts CI and the workflows call, each with a `test_*.py` beside it.
- `.github/workflows/` and `.github/actions/` run CI, ingest, archive and publish
  ([ADR-0003](docs/adr/0003-security-and-trust-boundaries.md), [ADR-0007](docs/adr/0007-cli-artifact-handoff.md)).
- `docs/SPEC.md` says what v0.1 does. `docs/adr/` records the decisions.

## Commands

```bash
# .NET (what CI runs)
dotnet restore DcLedger.slnx --locked-mode
dotnet format DcLedger.slnx --verify-no-changes --no-restore
dotnet build DcLedger.slnx --no-restore -c Release
dotnet test --solution DcLedger.slnx --no-build -c Release

# Site (from site/)
npm ci && npm run format:check && npm run fixture && npm run check && npm run build && npm test

# Python tools
python3 -m unittest discover -s tools -p 'test_*.py'
```

## Rules for every change

- **Never fabricate a source.** Every record points to a document that was actually retrieved. If you can't open a
  source, say so; don't guess a date, a vote, a URL or a hash.
- **Tests:** follow [ADR-0006](docs/adr/0006-testing.md). Every bug fix gets a regression test unless that needs a
  complicated harness. Never delete or loosen an existing assertion to get CI green; flag it to a human instead.
- **Errors:** follow [ADR-0005](docs/adr/0005-error-handling.md). The validator returns errors and never throws.
- **Keep code modular:** small types with one job, dependencies pointing inward, no new god classes.
- **Workflows:** pin every action to a full commit SHA, keep `permissions:` minimal per job, no
  `pull_request_target` (ADR-0003).
- **Commits** need a `Signed-off-by:` line matching the author or committer (the DCO check in CI).

## Maintainer practice modes

This section applies only when the person you're working with is the maintainer and they name a mode, either in
their message or in their own session instructions. Contributors can ignore it: your pull requests may be written
however you like, and they're reviewed against the rules above.

The maintainer uses this project to keep his own design, coding and debugging skills sharp. In these modes, the
agent's job is to make him faster at thinking, not to do the thinking.

### Human-led paths

- `src/DcLedger.Domain/`, `src/DcLedger.Application/`, `src/DcLedger.Publishing/`
- `.github/workflows/`, `.github/actions/`
- `tools/*.py` except `tools/test_*.py`
- `docs/adr/`

Everywhere else (`src/DcLedger.Cli/` option wiring, `src/DcLedger.Infrastructure/`, adapters, fixtures, `site/`,
`data/`, docs other than ADRs, CHANGELOG, dependency bumps) the agent may write code in any mode.

### Modes

| Mode | Who writes code in human-led paths | What the agent does |
| --- | --- | --- |
| `mode: solo` | The maintainer, with no help until he says he's done | Nothing until asked. Then reviews and quizzes only |
| `mode: coached` | The maintainer | Asks questions, gives hints, proposes test cases, reviews, writes the plumbing outside human-led paths |
| `mode: pair` | The agent writes scaffolding; the maintainer fills every decision point | Leaves `TODO(human)` at each decision and stops |
| `mode: delegate` | The agent | Does the whole task, as usual |

If no mode is named, use `delegate`. If he names a mode once, keep it for the rest of the session until he changes it.
Say which mode you're in at the start of each task.

In `solo` and `coached`, **never write or rewrite implementation code in a human-led path** unless the message
contains `UNLOCK` (for example `UNLOCK src/DcLedger.Application/KnownDates.cs`). The unlock covers only the path
named and only that request. If you notice you're about to write such code, stop and give a hint instead.

### How to help in `coached` and `pair`

- **Ask first.** Before proposing a design or a fix, ask what he thinks and why. Ask one question at a time.
- **Hints climb a ladder.** When he says `hint` (or `hint 2`, `hint 3`), give exactly one hint at that level and stop:
  1 = the concept or invariant he's probably missing; 2 = the file, type or API to look at, and why; 3 = pseudocode
  for the smallest next step, not C#. Never skip a level he didn't ask for.
- **Decision points get `TODO(human)`.** Error handling, data shapes, state transitions, idempotency rules, Eastern-time
  edge cases, workflow triggers and permissions: write `// TODO(human): <the decision, its constraints, the ADR>` (or
  `# TODO(human):` in YAML and Python) and stop.
- **Tests:** he writes the first failing tests for logic in human-led paths. When he says `adversary tests`, list up
  to 10 inputs that would break a plausible implementation (test name, input, expected result) and write no
  implementation.
- **CI failures:** when he says `ci clues <run id>`, report the failing job and step and quote the ~10 most relevant
  log lines. Name 2–3 places worth looking. Then ask for his hypothesis. Only after he answers, say whether the
  evidence supports it and what single check would confirm or rule it out. Don't propose a fix first.
- **Design:** when he says `challenge <ADR or doc>`, act as a staff-level reviewer. Ask at most 7 questions, one at a
  time (failure modes, data evolution, idempotency, trust boundaries, time zones), and don't give your answer until
  he has given his.
- **Review:** when he says `review my diff`, review `git diff main...HEAD` against the rules above, ADR-0001, ADR-0005
  and SOLID. Rank findings blocker, major, minor, nit. For each: `file:line`, the risk, and a question that leads him
  to the fix. No patches. Treat a deleted or weakened assertion as a blocker.
- **Retro:** when he says `retro`, ask three questions about the change (one debugging, one code-reading, one
  conceptual), grade his answers, then turn his notes into an addition of five lines or fewer to this file.
- **Commits:** add an `Assist: solo|coached|pair|delegate` trailer to every commit in a practice session, so the mix
  shows up in `git log --grep='Assist: '`.
