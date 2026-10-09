import { createServiceClient, type ServicePaths } from "@acme/api-clients";
import type { ServiceKey } from "../generated/platform";
import { readProblem, ApiError, type ProblemDetails } from "./problem";

const csrfHeader = "X-CSRF";
const idempotencyHeader = "Idempotency-Key";

/** Where the BFF starts a sign-in. Relative, like every URL: the app and the API share one origin. */
export const loginPath = "/bff/login";

type Redirect = (url: string) => void;
let redirect: Redirect = (url) => window.location.assign(url);

/** Tests replace how the page is sent to sign in. */
export function setRedirect(next: Redirect): void {
  redirect = next;
}

export function signInUrl(returnTo: string = window.location.pathname + window.location.search): string {
  return `${loginPath}?returnUrl=${encodeURIComponent(returnTo)}`;
}

function newKey(): string {
  return crypto.randomUUID();
}

/**
 * The one `fetch` every API call goes through: same-origin cookies (there are no tokens in JavaScript), the CSRF header on
 * anything that changes data, an Idempotency-Key on every POST so a repeated click or retry can't create twice, ProblemDetails
 * turned into {@link ApiError}, and a redirect to sign-in when the session has ended.
 */
export async function platformFetch(input: Request): Promise<Response> {
  const method = input.method.toUpperCase();
  const safe = method === "GET" || method === "HEAD" || method === "OPTIONS";
  const request = new Request(input, { credentials: "same-origin" });
  if (!safe) request.headers.set(csrfHeader, "1");
  if (method === "POST" && !request.headers.has(idempotencyHeader)) request.headers.set(idempotencyHeader, newKey());
  if (!request.headers.has("Accept-Language") && typeof document !== "undefined")
    request.headers.set("Accept-Language", document.documentElement.lang || "en");

  const response = await fetch(request);
  if (response.status === 401) {
    redirect(signInUrl());
    // Never resolves: the page is leaving. (Callers don't need an error path for "your session ended".)
    return new Promise<Response>(() => undefined);
  }
  return response;
}

/** A typed client for one service. Paths are relative (`/api/{service}/v1/...`), so no host is ever in code. */
export function api<K extends keyof ServicePaths>(service: K) {
  // The page's own origin: requests are same-origin, so cookies flow and no host name is ever written in code.
  return createServiceClient(service, { baseUrl: window.location.origin, fetch: platformFetch });
}

/** Result of an openapi-fetch call. */
interface Result<T> {
  data?: T;
  error?: unknown;
  response: Response;
}

/** Awaits an API call and returns its data; a failed call throws an {@link ApiError}. */
export async function unwrap<T>(call: Promise<Result<T>>): Promise<T> {
  const { data, error, response } = await call;
  // openapi-fetch has already read the body of a failed response into `error`.
  if (!response.ok) throw new ApiError(response.status, asProblem(error));
  return data as T;
}

/** Same for calls with no response body. */
export async function unwrapEmpty(call: Promise<Result<unknown>>): Promise<void> {
  const { error, response } = await call;
  if (!response.ok) throw new ApiError(response.status, asProblem(error));
}

function asProblem(error: unknown): ProblemDetails | undefined {
  return typeof error === "object" && error !== null ? (error as ProblemDetails) : undefined;
}

/** The path of a service endpoint: `/api/{service-key}/{path}`. The only place that spells the prefix. */
export function apiPath(service: ServiceKey, path: string): string {
  return `/api/${service}/${path.replace(/^\//, "")}`;
}

/** A JSON call for an endpoint that has no generated type yet (or whose shape the caller owns). Prefer {@link api}. */
export async function apiJson<T>(service: ServiceKey, path: string, init: { method?: string; body?: unknown } = {}): Promise<T> {
  const response = await platformFetch(
    new Request(new URL(apiPath(service, path), window.location.origin), {
      method: init.method ?? "GET",
      headers: { Accept: "application/json", ...(init.body !== undefined ? { "Content-Type": "application/json" } : {}) },
      body: init.body !== undefined ? JSON.stringify(init.body) : null,
    }),
  );
  if (!response.ok) throw await readProblem(response);
  return (await response.json()) as T;
}

/** A plain JSON GET for the BFF's own endpoints (`/bff/user`, `/bff/config`). */
export async function getJson<T>(path: string, options: { allowUnauthenticated?: boolean } = {}): Promise<T | null> {
  const response = await fetch(path, { credentials: "same-origin", headers: { Accept: "application/json" } });
  if (response.status === 401 && options.allowUnauthenticated) return null;
  if (response.status === 401) {
    redirect(signInUrl());
    return new Promise<T | null>(() => undefined);
  }
  if (!response.ok) throw await readProblem(response);
  return (await response.json()) as T;
}

export { ApiError };
