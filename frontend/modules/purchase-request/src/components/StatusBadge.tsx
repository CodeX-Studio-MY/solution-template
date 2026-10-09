import { useTranslation } from "react-i18next";
import { StatusChip, type Tone } from "@acme/ui-kit";
import type { RequestStatus } from "../types";

const tones: Record<RequestStatus, Tone> = { draft: "default", submitted: "info", approved: "success", rejected: "error" };

/** The request's status as text and colour. */
export function StatusBadge({ status }: { status: RequestStatus }) {
  const { t } = useTranslation("purchase-request");
  return <StatusChip label={t(`status.${status}`)} tone={tones[status]} />;
}
