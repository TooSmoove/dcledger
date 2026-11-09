import { defineCollection } from 'astro:content';
import { file } from 'astro/loaders';
import { z } from 'astro/zod';
import { SITE_DATA_FILE, actionsFrom, eventsFrom, jurisdictionsFrom } from './lib/site-data.js';

const sha256 = z.string().regex(/^[0-9a-f]{64}$/);
const documents = z
  .array(z.object({ url: z.url(), retrieved_utc: z.iso.datetime(), sha256 }))
  .min(1);

const events = defineCollection({
  loader: file(SITE_DATA_FILE, { parser: eventsFrom }),
  schema: z.object({
    id: z.string(),
    uid: z.string(),
    jurisdiction_id: z.string(),
    body: z.string(),
    title: z.string(),
    case_number: z.string().nullish(),
    starts_utc: z.iso.datetime(),
    starts_local: z.iso.datetime({ offset: true }),
    status: z.enum(['scheduled', 'cancelled']),
    sequence: z.number().int().nonnegative(),
    label: z.enum(['reported_news', 'confirmed_official']),
    location: z.object({ lat: z.number(), lon: z.number() }).nullable(),
    documents,
  }),
});

const jurisdictions = defineCollection({
  loader: file(SITE_DATA_FILE, { parser: jurisdictionsFrom }),
  schema: z.object({
    id: z.string(),
    name: z.string(),
    last_checked_utc: z.iso.datetime().nullable(),
    stale: z.boolean(),
    // 48, or 168 for a jurisdiction checked by hand (issue #27); defaults for an events.json written before the field.
    stale_after_hours: z.number().int().positive().default(48),
  }),
});

// Decisions such as a moratorium vote (issue #25). Tokens mirror DcLedger.Domain.EnumTokens.
const actions = defineCollection({
  loader: file(SITE_DATA_FILE, { parser: actionsFrom }),
  schema: z.object({
    id: z.string(),
    jurisdiction_id: z.string(),
    event_id: z.string().nullable(),
    action_type: z.enum(['moratorium', 'rezoning', 'text_amendment', 'resolution']),
    outcome: z.enum(['adopted', 'adopted_amended', 'failed', 'continued', 'tabled']),
    decided_on: z.iso.date(),
    votes: z
      .string()
      .regex(/^\d{1,2}-\d{1,2}$/)
      .nullable(),
    // Reported unanimous, possibly before the tally is posted. Defaults for an events.json written before the field.
    unanimous: z.boolean().default(false),
    // The decision's revision (issue #90); defaults for an events.json written before the field.
    sequence: z.number().int().nonnegative().default(0),
    label: z.enum(['reported_news', 'confirmed_official']),
    documents,
  }),
});

export const collections = { events, jurisdictions, actions };
