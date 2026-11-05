import type { Tone } from "@saaskit/ui-kit";
import type { AccessStatus, TenantStatus } from "../api";

export const statusTone: Record<TenantStatus, Tone> = {
  provisioning: "info",
  active: "success",
  suspended: "warning",
  pendingDeletion: "error",
  deleted: "default",
};

export const accessTone: Record<AccessStatus, Tone> = {
  active: "success",
  suspended: "warning",
  revoked: "error",
};
