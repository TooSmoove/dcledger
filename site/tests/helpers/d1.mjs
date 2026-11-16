// A stand-in for a Cloudflare D1 binding, backed by node:sqlite (D1 is SQLite) with the real schema applied, so the
// Functions' SQL runs as written. Implements only what the Functions use: prepare().bind().first()/run()/all().
import { readFileSync } from 'node:fs';
import { DatabaseSync } from 'node:sqlite';

const schema = readFileSync(new URL('../../db/reports-schema.sql', import.meta.url), 'utf8');

export function testD1() {
  const db = new DatabaseSync(':memory:');
  db.exec(schema);
  return {
    sqlite: db,
    prepare(sql) {
      const statement = db.prepare(sql);
      const bound = (values) => ({
        async first() {
          return statement.get(...values) ?? null;
        },
        async run() {
          return statement.run(...values);
        },
        async all() {
          return { results: statement.all(...values) };
        },
      });
      return { bind: (...values) => bound(values), ...bound([]) };
    },
  };
}
