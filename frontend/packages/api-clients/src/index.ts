import createClient, { type Client, type ClientOptions } from "openapi-fetch";
import type { ServicePaths } from "./service-paths";

export type { ServicePaths };
export * from "./schemas";

/**
 * A typed client for one service: paths, parameters, bodies and responses all come from that service's OpenAPI document.
 * `@farm360/web-core` wraps this with the platform's `fetch` (CSRF, Idempotency-Key, ProblemDetails, sign-in redirect);
 * modules call `api("goods-receipt")` from there, not this function.
 */
export function createServiceClient<K extends keyof ServicePaths>(_service: K, options: ClientOptions): Client<ServicePaths[K]> {
  return createClient<ServicePaths[K]>(options);
}
