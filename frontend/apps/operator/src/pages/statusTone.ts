import type { Tone } from "@saaskit/ui-kit";
import type { TenantStatus } from "../api";

export const statusTone: Record<TenantStatus, Tone> = {
  provisioning: "info",
  active: "success",
  suspended: "warning",
  pendingDeletion: "error",
  deleted: "default",
};
