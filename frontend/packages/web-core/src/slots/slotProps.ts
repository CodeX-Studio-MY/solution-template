/**
 * The named places modules leave open for other modules, and what each passes to whoever fills it. This file is the only thing
 * the two sides share, so neither imports the other. Add a slot by adding a line, named `{owning-module-key}.{place}`, for example
 * `"goods-receipt.detail.side-panel": { receiptId: string; total: Money };`.
 */
export interface SlotProps {
  /** A card on the shell's home page. Modules contribute what needs a person's attention; the shell lays the cards out. */
  "home.dashboard": Record<string, never>;
}

export type SlotName = keyof SlotProps;
