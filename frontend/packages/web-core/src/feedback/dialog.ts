import { create } from "zustand";

export interface ConfirmOptions {
  title: string;
  text?: string;
  /** Default: "OK" (translated by the host). */
  confirmLabel?: string;
  cancelLabel?: string;
  /** `danger` for what can't be undone: the confirm button turns red and Cancel has the focus. */
  tone?: "default" | "danger";
}

export type AlertOptions = Omit<ConfirmOptions, "cancelLabel" | "tone">;

export interface DialogRequest extends ConfirmOptions {
  id: number;
  kind: "confirm" | "alert";
  resolve: (confirmed: boolean) => void;
}

interface DialogState {
  queue: DialogRequest[];
  open: (request: Omit<DialogRequest, "id">) => void;
  close: (id: number, confirmed: boolean) => void;
}

let nextId = 1;

/** The shell's `DialogHost` shows the first request; a second one waits until the first is answered. */
export const useDialogStore = create<DialogState>((set, get) => ({
  queue: [],
  open: (request) => set((state) => ({ queue: [...state.queue, { ...request, id: nextId++ }] })),
  close: (id, confirmed) => {
    get()
      .queue.find((r) => r.id === id)
      ?.resolve(confirmed);
    set((state) => ({ queue: state.queue.filter((r) => r.id !== id) }));
  },
}));

/** Ask, then act on the answer: `if (await dialog.confirm({ title, tone: "danger" })) remove()`. */
export const dialog = {
  confirm: (options: ConfirmOptions): Promise<boolean> =>
    new Promise((resolve) => useDialogStore.getState().open({ ...options, kind: "confirm", resolve })),
  alert: (options: AlertOptions): Promise<void> =>
    new Promise((resolve) => useDialogStore.getState().open({ ...options, kind: "alert", resolve: () => resolve() })),
};

export type Dialog = typeof dialog;

export function useDialog(): Dialog {
  return dialog;
}
