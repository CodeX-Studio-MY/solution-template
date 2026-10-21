import type { IdentitySchemas, TenancySchemas } from "@saaskit/api-clients";
import { useTranslation } from "react-i18next";
import { api, masterKey, modules, useRouteAccess } from "@saaskit/web-core";

export const identity = api("identity");
export const tenancy = api("tenancy");

export type Principal = IdentitySchemas["schemas"]["PrincipalDto"];
export type Group = IdentitySchemas["schemas"]["GroupDto"];
export type Assignment = IdentitySchemas["schemas"]["AssignmentDto"];
export type SupportAccess = TenancySchemas["schemas"]["SupportAccessDto"];
export type TenantSummary = TenancySchemas["schemas"]["TenantSummary"];
export type Enrollment = IdentitySchemas["schemas"]["MachineEnrollmentDto"];

/** Every access change can affect any list of people, groups or roles: refresh all of identity's queries and masters. */
export const identityQueries = [["identity"], masterKey("identity")];
export const tenancyQueries = [["tenancy"], masterKey("tenancy")];

/** Writes in access-management need `admin`; reads need `manager`. The buttons follow the same rule. */
export function useCanManageAccess(): boolean {
  return useRouteAccess("access-management", "admin");
}

export function useCanManageTenant(): boolean {
  return useRouteAccess("tenant-settings", "admin");
}

/** `autoApproveBelow` → "Auto approve below": the fallback label for a setting nobody translated yet. */
export function humanize(name: string): string {
  const words = name.replace(/([a-z0-9])([A-Z])/g, "$1 $2").toLowerCase();
  return words.charAt(0).toUpperCase() + words.slice(1);
}

/** A module's name: its own translation (`{module}:menu`) when its strings are loaded, else the name in platform.yaml. */
export function useModuleName(): (key: string) => string {
  const { t } = useTranslation();
  return (key) => t("menu", { ns: key, defaultValue: modules.find((m) => m.key === key)?.name ?? key });
}
