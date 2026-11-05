import type { TenancySchemas } from "@saaskit/api-clients";
import { api } from "@saaskit/web-core";

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
