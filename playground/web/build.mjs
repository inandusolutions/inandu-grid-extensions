#!/usr/bin/env node
// Bundles main.ts (+ @inandu-solutions/grid-angular and Angular from node_modules) into one
// self-contained file the ASP.NET Core app serves from wwwroot. Run `npm install` here first.
import { build, context } from 'esbuild';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { existsSync } from 'node:fs';

const here = dirname(fileURLToPath(import.meta.url));
const outfile = join(here, '../Inandu.Grid.Extensions.Playground/wwwroot/bundle.js');
const watch = process.argv.includes('--watch');

if (!existsSync(join(here, 'node_modules/@inandu-solutions/grid-angular'))) {
  console.error('error: run `npm install` in playground/web first.');
  process.exit(1);
}

/** @type {import('esbuild').BuildOptions} */
const options = {
  entryPoints: [join(here, 'main.ts')],
  outfile,
  bundle: true,
  format: 'esm',
  target: 'es2022',
  sourcemap: true,
  logLevel: 'info',
  // Angular's decorators need TS's legacy transform, not esbuild's stage-3 default.
  tsconfigRaw: { compilerOptions: { experimentalDecorators: true, useDefineForClassFields: false } },
};

if (watch) {
  const ctx = await context(options);
  await ctx.watch();
  console.log('watching…');
} else {
  await build(options);
  console.log('built', outfile);
}
