import type { Money } from "../format/formatters";

/**
 * The named places modules leave open for other modules, and what each passes to whoever fills it. This file is the only thing
 * the two sides share, so neither imports the other. Add a slot by adding a line.
 */
export interface SlotProps {
  "purchase-request.detail.side-panel": { requestId: string; costCenter?: string; total: Money };
}

export type SlotName = keyof SlotProps;
