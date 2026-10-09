import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";
import { useTranslation } from "react-i18next";
import { useFormatters } from "@acme/web-core";
import type { RequestDto } from "../types";

interface Step {
  key: string;
  label: string;
  at: string;
  note?: string;
}

/** What happened to a request, oldest first: created, submitted, each approval, then the decision. */
export function ApprovalTimeline({ request }: { request: RequestDto }) {
  const { t } = useTranslation("purchase-request");
  const format = useFormatters();

  const steps: Step[] = [{ key: "created", label: t("timeline.created"), at: request.createdOn }];
  if (request.submittedOn) steps.push({ key: "submitted", label: t("timeline.submitted"), at: request.submittedOn });
  for (const approval of request.approvals) {
    steps.push({
      key: `approval-${approval.level}-${approval.approverId}`,
      label: t("timeline.approvedStep", { level: approval.level }),
      at: approval.approvedOn,
    });
  }
  if (request.decidedOn) {
    if (request.status === "approved") {
      steps.push({
        key: "approved",
        label: request.autoApproved ? t("timeline.autoApproved") : t("timeline.approved"),
        at: request.decidedOn,
      });
    } else if (request.status === "rejected") {
      steps.push({
        key: "rejected",
        label: t("timeline.rejected"),
        at: request.decidedOn,
        ...(request.rejectionReason ? { note: request.rejectionReason } : {}),
      });
    }
  }
  if (request.goodsReceivedOn) steps.push({ key: "received", label: t("timeline.goodsReceived"), at: request.goodsReceivedOn });

  return (
    <Stack component="ol" spacing={1.5} aria-label={t("timeline.title")} sx={{ listStyle: "none", p: 0, m: 0 }}>
      {steps.map((step) => (
        <li key={step.key}>
          <Typography variant="body1">{step.label}</Typography>
          <Typography variant="caption" color="text.secondary">
            {format.dateTime(step.at)}
          </Typography>
          {step.note ? <Typography variant="body2">{step.note}</Typography> : null}
        </li>
      ))}
    </Stack>
  );
}
