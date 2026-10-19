// Lays the built frontend out the way the Gateway serves it:
//   dist/shell/                        the shell app (Bff:ShellRoot)
//   dist/mfe/{module-key}/{version}/   each module (Bff:ModulesRoot), immutable per version
// MODULE_VERSION defaults to the catalog's default module version, so a fresh tenant gets the modules built here.
import { cp, mkdir, readdir, rm } from "node:fs/promises";
import { existsSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const version = process.env.MODULE_VERSION ?? "0.0.0-dev";
const out = join(root, "dist");

await rm(out, { recursive: true, force: true });
await cp(join(root, "apps", "shell", "dist"), join(out, "shell"), { recursive: true });

// A product with no business module yet has no modules to stage (and git keeps no empty folder).
const modules = join(root, "modules");
for (const entry of existsSync(modules) ? await readdir(modules, { withFileTypes: true }) : []) {
  const dist = join(root, "modules", entry.name, "dist");
  if (!entry.isDirectory() || !existsSync(dist)) continue;
  const target = join(out, "mfe", entry.name, version);
  await mkdir(dirname(target), { recursive: true });
  await cp(dist, target, { recursive: true });
  console.log(`staged ${entry.name} ${version}`);
}
console.log(`staged shell -> ${join(out, "shell")}`);
