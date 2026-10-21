import type { ComponentType } from "react";
import { canAccess, type ModuleKey, type ModuleManifest, type ModuleRoles, type Role } from "@saaskit/web-core";

export type SettingsGroup = "organization" | "access" | "applications";

/** One page of the Settings area. The list below is the single source for the menu, the routes and the top bar's gear. */
export interface SettingsSection {
  path: string;
  group: SettingsGroup;
  /** An i18n key in `ns`. */
  label: string;
  ns: string;
  /** Shown to people with at least `role` in `module` (the API applies the same rule). */
  module: ModuleKey | (string & {});
  role: Role;
  lazy: () => Promise<{ Component: ComponentType }>;
}

const builtIn: SettingsSection[] = [
  {
    path: "profile",
    group: "organization",
    label: "sections.profile",
    ns: "settings",
    module: "tenant-settings",
    role: "user",
    lazy: () => import("./pages/ProfilePage"),
  },
  {
    path: "branding",
    group: "organization",
    label: "sections.branding",
    ns: "settings",
    module: "tenant-settings",
    role: "admin",
    lazy: () => import("./pages/BrandingPage"),
  },
  {
    path: "applications",
    group: "organization",
    label: "sections.modules",
    ns: "settings",
    module: "tenant-settings",
    role: "admin",
    lazy: () => import("./pages/ModuleSettingsPage"),
  },
  {
    path: "features",
    group: "organization",
    label: "sections.features",
    ns: "settings",
    module: "tenant-settings",
    role: "admin",
    lazy: () => import("./pages/FeaturesPage"),
  },
  {
    path: "support",
    group: "organization",
    label: "sections.support",
    ns: "settings",
    module: "tenant-settings",
    role: "admin",
    lazy: () => import("./pages/SupportAccessPage"),
  },
  {
    path: "activity",
    group: "organization",
    label: "sections.activity",
    ns: "settings",
    module: "tenant-settings",
    role: "admin",
    lazy: () => import("./pages/ActivityPage"),
  },
  {
    path: "users",
    group: "access",
    label: "sections.users",
    ns: "settings",
    module: "access-management",
    role: "manager",
    lazy: () => import("./pages/UsersPage"),
  },
  {
    path: "groups",
    group: "access",
    label: "sections.groups",
    ns: "settings",
    module: "access-management",
    role: "manager",
    lazy: () => import("./pages/GroupsPage"),
  },
  {
    path: "devices",
    group: "access",
    label: "sections.machines",
    ns: "settings",
    module: "access-management",
    role: "manager",
    lazy: () => import("./pages/MachinesPage"),
  },
  {
    path: "audit",
    group: "access",
    label: "sections.audit",
    ns: "settings",
    module: "access-management",
    role: "admin",
    lazy: () => import("./pages/SecurityLogPage"),
  },
];

/** The platform's pages, then each loaded module's own (`manifest.settings`), under `/settings/{module}/{path}`. */
export function settingsSections(manifests: readonly ModuleManifest[]): SettingsSection[] {
  return [
    ...builtIn,
    ...manifests.flatMap((manifest) =>
      (manifest.settings ?? []).map((page) => ({
        path: `${manifest.id}/${page.path}`,
        group: "applications" as const,
        label: page.label,
        ns: manifest.i18n.namespace,
        module: manifest.id,
        role: page.requiredRole,
        lazy: page.lazy,
      })),
    ),
  ];
}

export function visibleSections(sections: readonly SettingsSection[], roles: ModuleRoles): SettingsSection[] {
  return sections.filter((section) => canAccess(roles, section.module, section.role));
}
