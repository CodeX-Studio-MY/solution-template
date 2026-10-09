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

for (const entry of await readdir(join(root, "modules"), { withFileTypes: true })) {
  const dist = join(root, "modules", entry.name, "dist");
  if (!entry.isDirectory() || !existsSync(dist)) continue;
  const target = join(out, "mfe", entry.name, version);
  await mkdir(dirname(target), { recursive: true });
  await cp(dist, target, { recursive: true });
  console.log(`staged ${entry.name} ${version}`);
}
console.log(`staged shell -> ${join(out, "shell")}`);
