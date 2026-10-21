import { create } from "zustand";
import { ApiError } from "../http/problem";

export type Severity = "success" | "info" | "warning" | "error";

export interface Snack {
  id: number;
  message: string;
  severity: Severity;
}

interface SnackbarState {
  queue: Snack[];
  push: (message: string, severity: Severity) => void;
  dismiss: (id: number) => void;
}

let nextId = 1;

/** The shell's `SnackbarHost` shows the first message in the queue; the rest wait their turn. */
export const useSnackbarStore = create<SnackbarState>((set) => ({
  queue: [],
  push: (message, severity) => set((state) => ({ queue: [...state.queue, { id: nextId++, message, severity }] })),
  dismiss: (id) => set((state) => ({ queue: state.queue.filter((snack) => snack.id !== id) })),
}));

function push(severity: Severity) {
  return (message: string) => useSnackbarStore.getState().push(message, severity);
}

/** Short messages at the edge of the screen. Callable anywhere, also outside React (a store, an event handler). */
export const snackbar = {
  success: push("success"),
  info: push("info"),
  warning: push("warning"),
  error: push("error"),
  /** A failed call: the server's localized message (ProblemDetails), or the error's own. */
  fromError: (error: unknown) => {
    const message = error instanceof ApiError || error instanceof Error ? error.message : String(error);
    useSnackbarStore.getState().push(message, "error");
  },
};

export type Snackbar = typeof snackbar;

/** `const notify = useSnackbar(); notify.success(t("saved"));` */
export function useSnackbar(): Snackbar {
  return snackbar;
}
