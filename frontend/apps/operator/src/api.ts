import type { TenancySchemas } from "@saaskit/api-clients";
import { api, unwrap, useApiQuery } from "@saaskit/web-core";

export const tenancy = api("tenancy");

export type TenantSummary = TenancySchemas["schemas"]["TenantSummary"];
export type TenantStatus = TenancySchemas["schemas"]["TenantStatus"];
export type PrincipalType = TenancySchemas["schemas"]["PlatformPrincipalType"];
export type PrincipalSummary = TenancySchemas["schemas"]["PrincipalSummary"];
export type PrincipalDetail = TenancySchemas["schemas"]["PrincipalDetail"];
export type PrincipalInput = TenancySchemas["schemas"]["PrincipalInput"];
export type TenantPrincipalRow = TenancySchemas["schemas"]["TenantPrincipalRow"];
export type AccessAuditRow = TenancySchemas["schemas"]["AccessAuditRow"];
export type AccessStatus = TenancySchemas["schemas"]["TenantAccessStatus"];

export const tenantsKey = ["tenancy", "operator", "tenants"];
export const principalsKey = ["tenancy", "operator", "principals"];
export type PlanRow = TenancySchemas["schemas"]["PlanRow"];
export type PlanCreate = TenancySchemas["schemas"]["PlanCreate"];
export type ModuleRow = TenancySchemas["schemas"]["ModuleRow"];
export type IdentityProviderRow = TenancySchemas["schemas"]["IdentityProviderRow"];
export type LinkedProvider = PrincipalDetail["providers"][number];
export type LoginProvider = TenancySchemas["schemas"]["LoginProvider"];

export const plansKey = ["tenancy", "operator", "plans"];
export const modulesKey = ["tenancy", "operator", "modules"];
export const providersKey = ["tenancy", "operator", "identity-providers"];

/** The plans, lowest first. The database owns them: tenant dialogs offer these, never a list in code. */
export function usePlans() {
  return useApiQuery(plansKey, (signal) => unwrap(tenancy.GET("/api/tenancy/v1/operator/plans", { signal })));
}

/** The modules the platform has (read only: a module needs code). */
export function useModules() {
  return useApiQuery(modulesKey, (signal) => unwrap(tenancy.GET("/api/tenancy/v1/operator/modules", { signal })));
}
