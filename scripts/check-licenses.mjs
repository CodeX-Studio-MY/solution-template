// The license policy (SAAS_TEMPLATE_PROMPT section 1): nothing in the dependency tree may need a paid commercial license unless
// docs/license-exceptions.json records the decision (and docs/deviations.md explains it). It checks, for the .NET and the npm
// dependencies (direct and transitive): (1) a deny-list of libraries that are commercial, and (2) every license against an allow-list.
// Anything unknown fails until someone decides.
import { execFileSync } from "node:child_process";
import { existsSync, readFileSync, readdirSync, statSync } from "node:fs";
import { homedir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const exceptions = JSON.parse(readFileSync(join(root, "docs", "license-exceptions.json"), "utf8"));

/** Libraries that need a paid license (or have moved to one). `from` is the first version affected. */
const denied = {
  nuget: [
    { name: /^AutoMapper(\.|$)/i, reason: "AutoMapper is commercial since 2025" },
    { name: /^MediatR(\.|$)/i, reason: "MediatR is commercial since 2025" },
    { name: /^MassTransit(\.|$)/i, from: 9, reason: "MassTransit v9 and later is commercial" },
    { name: /^FluentAssertions(\.|$)/i, from: 8, reason: "FluentAssertions v8 and later is commercial" },
    { name: /^Duende\./i, reason: "Duende IdentityServer and BFF are commercial" },
  ],
  npm: [{ name: /^@mui\/x-.*-(pro|premium)$/, reason: "MUI X Pro and Premium are commercial; use the MIT edition" }, { name: /^@mui\/x-license/, reason: "MUI X license keys mean a commercial edition" }],
};

const allowed = new Set([
  "MIT", "MIT-0", "ISC", "BSD-2-Clause", "BSD-3-Clause", "0BSD", "Apache-2.0", "MPL-2.0", "Unlicense", "CC0-1.0", "CC-BY-4.0", "BlueOak-1.0.0",
  "Python-2.0", "Zlib", "PostgreSQL", "BSL-1.0", "Artistic-2.0", "OFL-1.1", "LGPL-2.1-only", "LGPL-3.0-only", "(MIT OR Apache-2.0)", "W3C-20150513",
]);

const problems = [];
const isAllowed = (expression) =>
  expression
    .replace(/[()]/g, " ")
    .split(/\s+(?:OR|AND)\s+/i)
    .map((part) => part.trim())
    .filter(Boolean)
    .every((part) => allowed.has(part)) || allowed.has(expression);
const excepted = (ecosystem, name) => exceptions.some((e) => e.ecosystem === ecosystem && (e.name === name || new RegExp(e.name).test(name)));
const major = (version) => Number.parseInt(String(version).split(".")[0], 10);

function check(ecosystem, name, version, license) {
  for (const rule of denied[ecosystem]) {
    if (rule.name.test(name) && (rule.from === undefined || major(version) >= rule.from) && !excepted(ecosystem, name))
      problems.push(`${ecosystem}: ${name} ${version}: ${rule.reason}`);
  }
  if (!license || !isAllowed(license)) {
    if (!excepted(ecosystem, name)) problems.push(`${ecosystem}: ${name} ${version}: license "${license || "unknown"}" is not on the allow-list`);
  }
}

// ---- .NET: every package in every project.assets.json, with the license from its nuspec in the global packages folder ----
const packagesFolder = process.env.NUGET_PACKAGES ?? join(homedir(), ".nuget", "packages");
const seen = new Map();
function assets(dir) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === "node_modules" || entry.name === ".git") continue;
    const path = join(dir, entry.name);
    if (entry.isDirectory()) assets(path);
    else if (entry.name === "project.assets.json") {
      const json = JSON.parse(readFileSync(path, "utf8"));
      for (const key of Object.keys(json.libraries ?? {})) {
        const [name, version] = key.split("/");
        if (json.libraries[key].type === "package") seen.set(`${name}/${version}`, { name, version });
      }
    }
  }
}
assets(join(root, "backend"));
for (const { name, version } of seen.values()) {
  const nuspec = join(packagesFolder, name.toLowerCase(), version, `${name.toLowerCase()}.nuspec`);
  let license = "";
  if (existsSync(nuspec)) {
    const text = readFileSync(nuspec, "utf8");
    license = /<license[^>]*type="expression"[^>]*>([^<]+)<\/license>/i.exec(text)?.[1] ?? (/<license[^>]*type="file"/i.test(text) ? "file" : /<licenseUrl>([^<]*)<\/licenseUrl>/i.exec(text)?.[1] ?? "");
  }
  check("nuget", name, version, license.trim());
}
if (seen.size === 0) problems.push("nuget: no project.assets.json found; run dotnet restore first");

// ---- npm: pnpm knows each installed package's license ----
if (existsSync(join(root, "frontend", "node_modules"))) {
  const pnpm = process.env.PNPM ?? (existsSync(join(homedir(), ".local", "bin", "pnpm")) ? join(homedir(), ".local", "bin", "pnpm") : "pnpm");
  const output = execFileSync(pnpm, ["licenses", "list", "--json"], { cwd: join(root, "frontend"), encoding: "utf8", maxBuffer: 64 * 1024 * 1024 });
  const byLicense = JSON.parse(output);
  for (const [license, packages] of Object.entries(byLicense)) {
    for (const pkg of packages) for (const version of pkg.versions ?? [pkg.version]) check("npm", pkg.name, version, license);
  }
} else {
  problems.push("npm: frontend/node_modules not found; run pnpm install first");
}

if (problems.length > 0) {
  console.error(`License policy: ${problems.length} problem(s)\n` + [...new Set(problems)].sort().map((p) => `  - ${p}`).join("\n"));
  console.error("\nIf a library is acceptable, record the decision in docs/license-exceptions.json and docs/deviations.md.");
  process.exit(1);
}
console.log(`License policy: ${seen.size} NuGet packages and the npm tree are within the policy.`);
