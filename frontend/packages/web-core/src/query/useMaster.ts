import { useMemo } from "react";
import { useQuery, type QueryKey } from "@tanstack/react-query";
import type { ServicePaths } from "@saaskit/api-clients";
import { platformFetch } from "../http/client";
import { readProblem } from "../http/problem";
import { queryClient } from "./queryClient";

type Service = keyof ServicePaths;
type Paths<S extends Service> = ServicePaths[S];

/** The paths of a service that answer GET (OpenAPI marks the others `get?: never`). */
export type ListPath<S extends Service> = {
  [P in keyof Paths<S>]: Paths<S>[P] extends { get: { responses: { 200: unknown } } } ? P : never;
}[keyof Paths<S>] &
  string;

type Json<R> = R extends { content: { "application/json": infer B } } ? B : never;
type Body<S extends Service, P extends ListPath<S>> = Paths<S>[P] extends { get: { responses: { 200: infer R } } } ? Json<R> : never;

/** One item of a list endpoint: either a plain array or the platform's `Page<T>` (`{ items }`). */
export type MasterItem<S extends Service, P extends ListPath<S>> =
  Body<S, P> extends readonly (infer I)[] ? I : Body<S, P> extends { items?: readonly (infer I)[] | null } ? I : never;

export interface MasterOption {
  value: string;
  label: string;
}

export interface MasterOptions<T> {
  /** Query-string parameters (filters, or a page size for `Page<T>` endpoints). */
  query?: Record<string, string | number | boolean | undefined>;
  /** How an item becomes a dropdown entry. Default: `id` and the first of `name`, `displayName`, `title`, `code`. */
  toOption?: (item: T) => MasterOption;
  /** Off while a dependency (a parent selection) isn't known yet. */
  enabled?: boolean;
}

export interface Master<T> {
  items: T[];
  options: MasterOption[];
  /** The item with this id (from `toOption`'s `value`), for showing a name where only the id is stored. */
  byId: (id: string | null | undefined) => T | undefined;
  /** The label for an id, or the id itself when it isn't in the list (deleted, or not loaded yet). */
  labelOf: (id: string | null | undefined) => string;
  isLoading: boolean;
  error: Error | null;
  refetch: () => Promise<unknown>;
}

/** Master data changes rarely: kept fresh for 5 minutes, then refreshed in the background. */
const masterStaleTime = 5 * 60_000;

/** The cache key of a master list. Pass it to `useApiMutation({ invalidate })` after changing that master. */
export function masterKey(service: Service, path?: string, query?: MasterOptions<unknown>["query"]): QueryKey {
  return ["master", service, ...(path ? [path] : []), ...(query ? [query] : [])];
}

/** Refreshes one master list (every query of it) or, without a path, all of a service's master lists. */
export function invalidateMaster(service: Service, path?: string): Promise<void> {
  return queryClient.invalidateQueries({ queryKey: masterKey(service, path) });
}

function defaultOption(item: unknown): MasterOption {
  const record = (item ?? {}) as Record<string, unknown>;
  const value = String(record.id ?? record.key ?? record.code ?? "");
  const label = [record.name, record.displayName, record.title, record.code].find((v) => typeof v === "string" && v.length > 0);
  return { value, label: (label as string | undefined) ?? value };
}

/**
 * Reference ("master") data for pickers and labels: farms, students, brands, warehouses... Any list endpoint of any service, typed
 * from its OpenAPI document, fetched once and shared by every page and module that asks for the same list.
 *
 * ```ts
 * const farms = useMaster("farm", "/api/farm/v1/farms");
 * <MasterSelect master={farms} label="Farm" value={farmId} onChange={setFarmId} />
 * farms.labelOf(row.farmId)
 * ```
 */
export function useMaster<S extends Service, P extends ListPath<S>>(
  service: S,
  path: P,
  options: MasterOptions<MasterItem<S, P>> = {},
): Master<MasterItem<S, P>> {
  const { query, toOption = defaultOption, enabled = true } = options;
  const result = useQuery({
    queryKey: masterKey(service, path, query),
    staleTime: masterStaleTime,
    enabled,
    queryFn: async ({ signal }) => {
      const url = new URL(path, window.location.origin);
      for (const [name, value] of Object.entries(query ?? {})) if (value !== undefined) url.searchParams.set(name, String(value));
      const response = await platformFetch(new Request(url, { headers: { Accept: "application/json" }, signal }));
      if (!response.ok) throw await readProblem(response);
      const body = (await response.json()) as unknown;
      // ponytail: one page only. For a master bigger than the endpoint's page size, filter on the server through `query`.
      return (Array.isArray(body) ? body : ((body as { items?: unknown[] }).items ?? [])) as MasterItem<S, P>[];
    },
  });

  return useMemo(() => {
    const items = result.data ?? [];
    const options = items.map((item) => toOption(item));
    const index = new Map(options.map((option, i) => [option.value, items[i]!]));
    const labels = new Map(options.map((option) => [option.value, option.label]));
    return {
      items,
      options,
      byId: (id) => (id ? index.get(id) : undefined),
      labelOf: (id) => (id ? (labels.get(id) ?? id) : ""),
      isLoading: result.isLoading,
      error: result.error,
      refetch: result.refetch,
    };
    // toOption is usually an inline function; the list only changes with the data.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [result.data, result.isLoading, result.error, result.refetch]);
}
