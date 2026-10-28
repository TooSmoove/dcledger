# Legistar fixtures

Government source data only, scrubbed of personal information (ADR-0006). Field names, types and formats are
Legistar's own; free text is synthetic.

- `guilford/`: **recorded** Oct 28, 2026 from `webapi.legistar.com/v1/guilford` for meetings dated Oct 1 to Dec 1,
  2026, by `tools/record_legistar_fixtures.py` on a GitHub runner. Scrubbing is an allowlist: titles, matter names,
  comments, locations, movers and seconders are replaced. Synthetic titles keep one signal: an item whose original
  text matched the keyword classifier says "about a data center" (meeting 1599, case 2026-439, a public hearing).
  Each item list keeps its first five items plus every classifier match. Note the body name
  `"Board of Commissioners Work Session "` with a trailing space (meeting 1630): it is Legistar's, kept on purpose.
- `derived/`: **hand-made** from recorded meeting 1598 for cases the recording doesn't contain:
  9001 no `EventTime`; 9002 a comment mentioning a cancellation; 9003 a 2:30 AM start on Mar 8, 2026 (skipped by
  daylight saving); 9004 a body that isn't configured; 9005 a date outside the requested window; 9006 a draft with no
  agenda file yet.

To re-record, run the tool where Legistar is reachable, review every changed value, and commit only scrubbed output.
