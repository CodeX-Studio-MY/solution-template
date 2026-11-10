import { useState } from "react";
import Paper from "@mui/material/Paper";
import Rating from "@mui/material/Rating";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableHead from "@mui/material/TableHead";
import TablePagination from "@mui/material/TablePagination";
import TableRow from "@mui/material/TableRow";
import { useTranslation } from "react-i18next";
import { unwrap, useApiQuery, useFormatters } from "@saaskit/web-core";
import { EmptyState, PageHeader, ProblemAlert, StatusChip, type Tone } from "@saaskit/ui-kit";
import { tenancy, type FeedbackRow } from "../api";

const tones: Record<FeedbackRow["status"], Tone> = { new: "warning", reviewed: "info", done: "success" };

/** What this organisation's people sent to the provider, newest first. The provider decides its status; here it is read-only. */
export function Component() {
  const { t } = useTranslation("settings");
  const format = useFormatters();
  const [paging, setPaging] = useState({ page: 1, pageSize: 25 });
  const list = useApiQuery(["tenancy", "feedback", paging], (signal) =>
    unwrap(tenancy.GET("/api/tenancy/v1/feedback", { params: { query: { Page: paging.page, PageSize: paging.pageSize } }, signal })),
  );
  const rows = list.data?.items ?? [];
  return (
    <>
      <PageHeader title={t("sections.feedback")} subtitle={t("feedback.subtitle")} />
      <ProblemAlert error={list.error ?? undefined} />
      {list.data && rows.length === 0 ? (
        <EmptyState title={t("feedback.empty")} />
      ) : (
        <Paper variant="outlined" sx={{ overflowX: "auto" }}>
          <Table size="small" aria-label={t("sections.feedback")}>
            <TableHead>
              <TableRow>
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
                  <TableCell>{row.senderName}</TableCell>
                  <TableCell>{t(`common:feedback.kinds.${row.kind}`)}</TableCell>
                  <TableCell>
                    {row.rating === null ? null : (
                      <Rating readOnly size="small" value={Number(row.rating)} aria-label={String(row.rating)} />
                    )}
                  </TableCell>
                  <TableCell sx={{ whiteSpace: "pre-wrap", wordBreak: "break-word", minWidth: 240 }}>{row.message}</TableCell>
                  <TableCell>{row.pagePath}</TableCell>
                  <TableCell>{format.dateTime(row.createdOn)}</TableCell>
                  <TableCell>
                    <StatusChip label={t(`common:feedback.statuses.${row.status}`)} tone={tones[row.status]} />
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <TablePagination
            component="div"
            count={Number(list.data?.totalCount ?? 0)}
            page={paging.page - 1}
            rowsPerPage={paging.pageSize}
            rowsPerPageOptions={[25, 50, 100]}
            onPageChange={(_, next) => setPaging({ ...paging, page: next + 1 })}
            onRowsPerPageChange={(e) => setPaging({ page: 1, pageSize: Number(e.target.value) })}
          />
        </Paper>
      )}
    </>
  );
}
