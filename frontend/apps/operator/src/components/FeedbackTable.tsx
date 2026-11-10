import MenuItem from "@mui/material/MenuItem";
import Rating from "@mui/material/Rating";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import { useTranslation } from "react-i18next";
import { unwrap, useApiMutation, useFormatters } from "@saaskit/web-core";
import { tokens } from "@saaskit/ui-kit";
import { feedbackKey, tenancy, type FeedbackRow, type FeedbackStatus } from "../api";

const statuses: readonly FeedbackStatus[] = ["new", "reviewed", "done"];

/** Feedback rows with a status menu on each. A change sends the row's ETag, so two people triaging the same item can't overwrite each other. */
export function FeedbackTable({ rows, showTenant = true }: { rows: readonly FeedbackRow[]; showTenant?: boolean }) {
  const { t } = useTranslation("operator");
  const format = useFormatters();
  const change = useApiMutation(
    ({ row, status }: { row: FeedbackRow; status: FeedbackStatus }) =>
      unwrap(
        tenancy.POST("/api/tenancy/v1/operator/feedback/{id}/status", {
          params: { path: { id: row.feedbackId }, header: { "If-Match": row.eTag } },
          body: { status },
        }),
      ),
    { invalidate: [feedbackKey], success: t("feedback.saved") },
  );
  return (
    <Table size="small" aria-label={t("feedback.title")} sx={{ "& td, & th": { height: tokens.density.rowCompact } }}>
      <TableHead>
        <TableRow>
          {showTenant ? <TableCell>{t("feedback.tenant")}</TableCell> : null}
          <TableCell>{t("feedback.sender")}</TableCell>
          <TableCell>{t("feedback.kind")}</TableCell>
          <TableCell>{t("feedback.rating")}</TableCell>
          <TableCell>{t("feedback.message")}</TableCell>
          <TableCell>{t("feedback.page")}</TableCell>
          <TableCell>{t("feedback.sentOn")}</TableCell>
          <TableCell>{t("feedback.status")}</TableCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {rows.map((row) => (
          <TableRow key={row.feedbackId}>
            {showTenant ? <TableCell>{row.tenantName || row.tenantSlug}</TableCell> : null}
            <TableCell>{row.senderName}</TableCell>
            <TableCell>{t(`common:feedback.kinds.${row.kind}`)}</TableCell>
            <TableCell>
              {row.rating === null ? null : <Rating readOnly size="small" value={Number(row.rating)} aria-label={`${row.rating} / 5`} />}
            </TableCell>
            <TableCell sx={{ whiteSpace: "pre-wrap", wordBreak: "break-word", minWidth: 240 }}>{row.message}</TableCell>
            <TableCell>{row.pagePath}</TableCell>
            <TableCell sx={{ whiteSpace: "nowrap" }}>{format.dateTime(row.createdOn)}</TableCell>
            <TableCell>
              <TextField
                select
                size="small"
                value={row.status}
                disabled={change.isPending}
                onChange={(e) => change.mutate({ row, status: e.target.value as FeedbackStatus })}
                slotProps={{ htmlInput: { "aria-label": t("feedback.changeStatus") } }}
                sx={{ minWidth: 130 }}
              >
                {statuses.map((status) => (
                  <MenuItem key={status} value={status}>
                    {t(`common:feedback.statuses.${status}`)}
                  </MenuItem>
                ))}
              </TextField>
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
