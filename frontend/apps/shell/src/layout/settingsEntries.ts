import { canAccess, type ModuleManifest, type ModuleRoles, type SettingsPage } from "@farm360/web-core";

/** One settings page as the shell shows it: where it lives and its translated names. */
export interface SettingsEntry {
  manifest: ModuleManifest;
  page: SettingsPage;
  to: string;
  group: string;
  label: string;
}

/** Where the shell mounts a module's settings page. */
export function settingsPath(manifest: ModuleManifest, page: SettingsPage): string {
  return `/settings${manifest.basePath}/${page.path}`;
}

/** The settings pages the person may open, in manifest order, with their group and label translated. */
export function settingsEntries(
  manifests: readonly ModuleManifest[],
  roles: ModuleRoles,
  t: (key: string, options: { ns: string }) => string,
): SettingsEntry[] {
  return manifests.flatMap((manifest) =>
    (manifest.settings ?? [])
      .filter((page) => canAccess(roles, manifest.id, page.requiredRole))
      .map((page) => ({
        manifest,
        page,
        to: settingsPath(manifest, page),
        group: t(page.group, { ns: manifest.i18n.namespace }),
        label: t(page.label, { ns: manifest.i18n.namespace }),
      })),
  );
}

/** Entries grouped by their (translated) group, keeping the order in which groups first appear. */
export function groupSettings(entries: readonly SettingsEntry[]): { group: string; entries: SettingsEntry[] }[] {
  const groups = new Map<string, SettingsEntry[]>();
  for (const entry of entries) groups.set(entry.group, [...(groups.get(entry.group) ?? []), entry]);
  return [...groups].map(([group, grouped]) => ({ group, entries: grouped }));
}
