import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { dirname, join } from "node:path";

/**
 * The packages shared between the shell and every module as singletons (one copy at runtime). The list is the contract; the
 * versions are never typed here: they are read from the installed packages, whose versions are pinned once in the pnpm catalog.
 */
export const sharedPackages = [
  "react",
  "react-dom",
  "react-router",
  "@mui/material",
  "@emotion/react",
  "@emotion/styled",
  "zustand",
  "i18next",
  "react-i18next",
  "@acme/web-core",
  "@acme/ui-kit",
] as const;

function installedVersion(packageName: string, from: string): string {
  const require = createRequire(join(from, "package.json"));
  // Read the package.json by walking up from the package's entry, because many packages don't export "./package.json".
  let directory = dirname(require.resolve(packageName));
  for (;;) {
    try {
      const manifest = JSON.parse(readFileSync(join(directory, "package.json"), "utf8")) as { name?: string; version: string };
      if (manifest.name === packageName) return manifest.version;
    } catch {
      // keep walking up
    }
    const parent = dirname(directory);
    if (parent === directory) throw new Error(`Cannot find the installed version of ${packageName}`);
    directory = parent;
  }
}

/**
 * Packages whose deep imports (`@mui/material/Button`, `react/jsx-runtime`) must resolve to the shared copy too. Without the
 * trailing-slash entry a module that imports a sub-path would bundle its own copy and break theming and hooks.
 */
const sharedSubpaths = ["react", "react-dom", "@mui/material"] as const;

/** The `shared` option for the Module Federation plugin, for the app or module in `from`. */
export function sharedSingletons(from: string) {
  const entries = sharedPackages.map((name) => {
    const version = installedVersion(name, from);
    return [name, { singleton: true, requiredVersion: `^${version}`, version }] as const;
  });
  const subpaths = sharedSubpaths.map((name) => {
    const version = installedVersion(name, from);
    return [`${name}/`, { singleton: true, requiredVersion: `^${version}`, version }] as const;
  });
  return Object.fromEntries([...entries, ...subpaths]);
}
