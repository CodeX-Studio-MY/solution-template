import {
  useMutation,
  useQuery,
  useQueryClient,
  type QueryKey,
  type UseMutationResult,
  type UseQueryOptions,
  type UseQueryResult,
} from "@tanstack/react-query";
import { ApiError } from "../http/problem";
import { snackbar } from "../feedback/snackbar";

/**
 * Loads data for a screen, cached under `key`. `load` is usually `unwrap(client.GET(...))`, so the result is typed by the
 * service's OpenAPI document. Errors are {@link ApiError}s.
 *
 * ```ts
 * const users = useApiQuery(["identity", "principals"], (signal) => unwrap(identity.GET("/api/identity/v1/principals", { signal })));
 * ```
 */
export function useApiQuery<T>(
  key: QueryKey,
  load: (signal: AbortSignal) => Promise<T>,
  options: Omit<UseQueryOptions<T, Error>, "queryKey" | "queryFn"> = {},
): UseQueryResult<T, Error> {
  return useQuery<T, Error>({ queryKey: key, queryFn: ({ signal }) => load(signal), ...options });
}

export interface ApiMutationOptions<TData, TVariables> {
  /** Keys to refresh afterwards (a prefix refreshes everything under it). */
  invalidate?: QueryKey[];
  /** Shown as a success snackbar. */
  success?: string | ((data: TData, variables: TVariables) => string);
  onSuccess?: (data: TData, variables: TVariables) => void | Promise<void>;
}

/**
 * Changes data. On success it refreshes `invalidate` and shows `success`; on failure it shows the server's (localized) message in a
 * snackbar, except for field errors, which the form shows next to each field (read them from `mutation.error`).
 */
export function useApiMutation<TData = unknown, TVariables = void>(
  mutate: (variables: TVariables) => Promise<TData>,
  options: ApiMutationOptions<TData, TVariables> = {},
): UseMutationResult<TData, Error, TVariables> {
  const client = useQueryClient();
  return useMutation<TData, Error, TVariables>({
    mutationFn: mutate,
    onSuccess: async (data, variables) => {
      await Promise.all((options.invalidate ?? []).map((queryKey) => client.invalidateQueries({ queryKey })));
      if (options.success) snackbar.success(typeof options.success === "function" ? options.success(data, variables) : options.success);
      await options.onSuccess?.(data, variables);
    },
    onError: (error) => {
      if (error instanceof ApiError && error.fieldErrors.length > 0) return;
      snackbar.fromError(error);
    },
  });
}
