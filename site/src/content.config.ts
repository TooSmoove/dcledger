import { defineCollection } from 'astro:content';
import { file } from 'astro/loaders';
import { z } from 'astro/zod';
import { SITE_DATA_FILE, eventsFrom, jurisdictionsFrom } from './lib/site-data.js';

const sha256 = z.string().regex(/^[0-9a-f]{64}$/);

const events = defineCollection({
  loader: file(SITE_DATA_FILE, { parser: eventsFrom }),
  schema: z.object({
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
    documents: z.array(z.object({ url: z.url(), retrieved_utc: z.iso.datetime(), sha256 })).min(1),
  }),
});

const jurisdictions = defineCollection({
  loader: file(SITE_DATA_FILE, { parser: jurisdictionsFrom }),
  schema: z.object({
    name: z.string(),
    last_checked_utc: z.iso.datetime().nullable(),
    stale: z.boolean(),
  }),
});

export const collections = { events, jurisdictions };
