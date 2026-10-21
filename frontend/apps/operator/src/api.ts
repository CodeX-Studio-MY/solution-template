import type { TenancySchemas } from "@saaskit/api-clients";
import { api } from "@saaskit/web-core";

export const tenancy = api("tenancy");

export type TenantSummary = TenancySchemas["schemas"]["TenantSummary"];
export type TenantStatus = TenancySchemas["schemas"]["TenantStatus"];

export const tenantsKey = ["tenancy", "operator", "tenants"];
