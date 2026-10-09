import { useCallback, useEffect, useRef, useState } from "react";
import type { ApiError } from "../http/problem";

export interface Resource<T> {
  data: T | undefined;
  error: ApiError | Error | undefined;
  loading: boolean;
  /** Loads again (after a change). Keeps the old data on screen meanwhile. */
  reload: () => Promise<void>;
}

/** Loads data when `deps` change. A small stand-in for a data library; enough for the template's screens. */
export function useResource<T>(load: (signal: AbortSignal) => Promise<T>, deps: readonly unknown[]): Resource<T> {
  const [state, setState] = useState<{ data: T | undefined; error: Error | undefined; loading: boolean }>({
    data: undefined,
    error: undefined,
    loading: true,
  });
  const loader = useRef(load);
  loader.current = load;
  const generation = useRef(0);

  const run = useCallback(async (signal: AbortSignal) => {
    const mine = ++generation.current;
    setState((s) => ({ ...s, loading: true }));
    try {
      const data = await loader.current(signal);
      if (mine === generation.current) setState({ data, error: undefined, loading: false });
    } catch (error) {
      if (mine === generation.current && !signal.aborted) setState((s) => ({ data: s.data, error: error as Error, loading: false }));
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void run(controller.signal);
    return () => controller.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps);

  const reload = useCallback(() => run(new AbortController().signal), [run]);
  return { ...state, reload };
}
