# Wording bank

Ready-made wording for things the project says publicly: the disclosure on `/methodology/`, corrections log entries,
and replies to correction reports. Pick the template that fits, fill every `[bracket]`, and edit freely. When nothing
here fits, write it fresh and, if the situation could come up again, add it here.

Every template follows the same rules as the rest of the ledger (SPEC §E):

- **Facts, not adjectives.** Say what is, with dates and numbers. No "independent", "unbiased", "trusted",
  "rigorous" or similar self-praise; readers judge that from the record.
- **No private individuals' names** (SPEC §E privacy). The publisher and maintainer are public roles; whoever
  reported a mistake is not named.
- **Only promise what one maintainer can keep.** If a commitment changes, change it here and on the page in the same
  pull request, and log the change.
- **Plain words, short sentences.** Readers include residents, reporters and officials.

## Disclosure ("Who publishes this")

Lives in `site/src/owner/disclosure.md`. The site's tests fail while it still holds the placeholder, so it can't go
live unwritten. Combine one block from each part: **publisher**, **funding**, **relationships**, then **changes**.

### Publisher

**P1. Company only (before outreach).**

> NC Data Center Ledger is published by [Company name], a [state] [LLC / corporation] based in [city], North
> Carolina. One person maintains it. The code and data are public at
> [github.com/TooSmoove/dcledger](https://github.com/TooSmoove/dcledger).

**P2. Company and named maintainer.**

> NC Data Center Ledger is published by [Company name], a [state] [LLC / corporation] based in [city], North
> Carolina. It is maintained by [Full name], [role at the company, for example "its owner"]. The code and data are
> public at [github.com/TooSmoove/dcledger](https://github.com/TooSmoove/dcledger).

**P3. Individual, no company.**

> NC Data Center Ledger is published and maintained by [Full name], who lives in [county], North Carolina. The code
> and data are public at [github.com/TooSmoove/dcledger](https://github.com/TooSmoove/dcledger).

When switching from P1 to P2, add a log entry (see "Page wording changed" below) so the change is on the record.

### Funding

**F1. Self-funded.**

> The project has no outside funding. [Company name / The maintainer] pays its costs, which are hosting, a domain
> and storage, about [$ amount] a month.

**F2. Grants or donations.**

> The project is funded by [Funder name] ([grant / donation], [$ amount], [start date] to [end date]). Funders have
> no say over what is recorded, how it is worded or when it is published, and they do not see items before readers
> do. Funding of [$ threshold] or more from one source is listed here.

**F3. Paid services alongside the free ledger.**

> [Company name] also sells [service, for example "custom alerts"]. Paying customers see the same records as
> everyone else, at the same time; no one can pay to add, change, delay or remove a record.

### Relationships

State any tie between the publisher or maintainer and anyone whose decisions or projects the ledger records:
data center developers or operators, utilities, landowners in a case, local governments, and advocacy groups on any
side.

**R1. None.**

> Neither [Company name] nor [the maintainer / its owner] has a financial or advisory relationship with any data
> center developer or operator, utility, landowner in a recorded case, local government, or advocacy group on these
> issues.

**R2. A relationship exists.**

> [Company name / The maintainer] [describe the relationship plainly, for example "provides software consulting to
> [Organization]" or "owns property in [county] within [distance] of a proposed site"]. Items involving
> [Organization / that site] are [reviewed by a second person before publishing / marked with this disclosure on
> their page].

**R3. A public position.**

> [The maintainer] has [spoken / written] publicly about data centers in North Carolina, for example [link, date].
> The ledger records what governments do, using the same rules for every jurisdiction and every side.

### Changes

> This section was last updated on [Month D, YYYY]. Changes to it are listed in the corrections log.

## Corrections log entries

One file per entry in `data/corrections/` (see `data/README.md`). The `summary` says what was wrong and what the
record says now, in one or two sentences of at most 500 characters. Name the record by its field or its meaning,
never by a person.

| Situation | `summary` |
|---|---|
| Wrong date or time | "The meeting was listed for [old date and time]; it is [new date and time]." |
| Wrong vote | "The vote was recorded as [old]; the [minutes / action detail] show [new]." |
| Wrong outcome | "The decision was recorded as [old outcome]; the [record] shows [new outcome]." |
| Wrong body | "The item was listed under [old body]; it was heard by [new body]." |
| Title wording | "The title said [old]; it now says [new], which matches the [agenda / case page]." |
| Wrong or missing source | "The item cited [old source]; it now cites [new source], which shows [what it shows]." |
| Label changed | "The item was labeled [old label]; [why it changes, for example "the cited source is a news report, not an official record"]." |
| Decision term | "The [term, for example "expiry date"] was recorded as [old]; the adopted text states [new]." |
| Meeting was not held | "The meeting was listed as scheduled; [the body / the official site] cancelled it on [date]." |
| Wrong jurisdiction | "The item was listed under [old jurisdiction]; it belongs to [new jurisdiction]." |
| Page wording changed | "The methodology page said [old]; it now says [new]." |

Notes:

- A **record is never deleted.** A meeting that should not have been listed is marked `cancelled` with an entry
  explaining why.
- A **label moving from reported to confirmed** because a new official record was archived is routine, not a
  correction. Log it only when the earlier label was wrong for the evidence held at the time.
- **Timing:** `reported_on` is the day the report arrived or the problem was found; `corrected_on` is the day the fix
  merged.

## Replies to correction reports

Post on the report's GitHub issue within 48 hours.

**Corrected.**

> Thanks for the report. The record is corrected in [#PR] and is live on the site now: [item link]. The corrections
> log lists it at [link to /methodology/#log].

**The record stands.**

> Thanks for the report. We checked [source] ([date]), and it shows [what it shows], which matches the record, so the
> record stays as it is. If you have a document that shows otherwise, add it here and we'll look again.

**Need more information.**

> Thanks for the report. Which [meeting / decision / field] do you mean, and is there a document that shows the
> correct value? A link to the government's own page is best.

**Still checking (when the fix can't be finished in 48 hours).**

> Thanks for the report. We're checking it against [source]. That needs [what is pending, for example "the posted
> minutes"], expected by [date]. We'll update this issue then.

**Out of scope.**

> Thanks for writing. The ledger records only [scope, for example "meetings, hearings and decisions on data center
> land use by local governments in its covered jurisdictions"], so this isn't something it will record. [Optional:
> where else to look.]

**Contains a private person's name.**

> Thanks for the report. We've edited it to remove a private person's name; the ledger doesn't store names of
> private individuals. The report itself is still open.
