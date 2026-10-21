import type { ReactNode } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ApiError } from "../http/problem";

/** Retries only what may succeed next time: network failures and 5xx, at most twice. A 4xx answer won't change. */
function retry(failureCount: number, error: unknown): boolean {
  if (error instanceof ApiError && error.status < 500) return false;
  return failureCount < 2;
}

/**
 * The one query cache. web-core is a Module Federation singleton, so the shell and every module share it: a list loaded on one
 * page is reused on the next, and invalidating a key refreshes it everywhere.
 */
export function createPlatformQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: { staleTime: 30_000, retry, refetchOnWindowFocus: false },
      mutations: { retry: false },
    },
  });
}

export const queryClient = createPlatformQueryClient();

/** Put once around the app (the shell does). Modules never add their own. */
export function PlatformQueryProvider({ children, client = queryClient }: { children: ReactNode; client?: QueryClient }) {
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}
