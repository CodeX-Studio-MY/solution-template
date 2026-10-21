import { useState, type ReactNode } from "react";
import { createPlatformQueryClient, PlatformQueryProvider } from "../query/queryClient";

/** A fresh query cache per test (no retries), so one test's data never leaks into the next. */
export function TestProviders({ children }: { children: ReactNode }) {
  const [client] = useState(() => {
    const created = createPlatformQueryClient();
    created.setDefaultOptions({ queries: { retry: false, staleTime: Infinity }, mutations: { retry: false } });
    return created;
  });
  return <PlatformQueryProvider client={client}>{children}</PlatformQueryProvider>;
}
