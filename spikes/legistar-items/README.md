# Spike S15 — Legistar event items (issue #2)

Throwaway. Answers: do agenda item titles carry enough signal for the keyword classifier, how is a cancelled meeting marked, and how often do titles name private people?

Needs Python 3.10+ (`python --version`; on Windows with Scoop: `scoop install python`). No packages to install.

```sh
python spikes/legistar-items/spike.py collect   # about 2 minutes; writes spikes/legistar-items/out/
```

Open `spikes/legistar-items/out/candidates.csv` in Excel. For every row:
- `relevant`: **y** if the item is about a data center (siting, rules, moratorium, incentives, power for one), else **n**. Use `meeting_url` if the title isn't enough.
- `private_name`: **y** if the title names a private person (not a company, official or agency), else **n**.

Save as CSV (keep the name), then:

```sh
python spikes/legistar-items/spike.py score     # prints aggregates only
```

Paste the printed block into issue #2.

**Privacy:** titles can name petitioners and owners. They are written only to `out/` on your machine (git-ignored) and never printed by `score`. Don't commit or share `candidates.csv`.
