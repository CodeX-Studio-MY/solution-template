import type { ComponentType } from "react";
import { canAccess, type ModuleKey, type ModuleRoles, type Role } from "@saaskit/web-core";

export type SettingsGroup = "organization" | "access";

/** One page of the platform Settings. The list below is the single source for its menu, its routes and the top bar's gear. */
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

/** The platform settings behind the gear. Each module's own settings are in the module (`/{module}/settings`). */
export const settingsSections: readonly SettingsSection[] = [
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
    path: "modules",
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
    label: "sections.devices",
    ns: "settings",
    module: "access-management",
    role: "manager",
    lazy: () => import("./pages/DevicesPage"),
  },
  {
    path: "applications",
    group: "access",
    label: "sections.applications",
    ns: "settings",
    module: "access-management",
    role: "manager",
    lazy: () => import("./pages/ApplicationsPage"),
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

export function visibleSections(sections: readonly SettingsSection[], roles: ModuleRoles): SettingsSection[] {
  return sections.filter((section) => canAccess(roles, section.module, section.role));
}
