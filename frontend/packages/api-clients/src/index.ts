import createClient, { type Client, type ClientOptions } from "openapi-fetch";
import type { ServicePaths } from "./service-paths";

export type { ServicePaths };
export type { components as PurchaseRequestSchemas } from "./generated/purchase-request";
export type { components as CostCenterBudgetSchemas } from "./generated/cost-center-budget";
export type { components as TenancySchemas } from "./generated/tenancy";
export type { components as IdentitySchemas } from "./generated/identity";
export type { components as NotificationSchemas } from "./generated/notification";

/**
 * A typed client for one service: paths, parameters, bodies and responses all come from that service's OpenAPI document.
 * `@acme/web-core` wraps this with the platform's `fetch` (CSRF, Idempotency-Key, ProblemDetails, sign-in redirect);
 * modules call `api("purchase-request")` from there, not this function.
 */
export function createServiceClient<K extends keyof ServicePaths>(_service: K, options: ClientOptions): Client<ServicePaths[K]> {
  return createClient<ServicePaths[K]>(options);
}
