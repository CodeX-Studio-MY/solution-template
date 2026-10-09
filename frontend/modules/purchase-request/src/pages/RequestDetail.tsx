import { useState } from "react";
import { Link as RouterLink, useNavigate, useParams } from "react-router";
import Button from "@mui/material/Button";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import Grid from "@mui/material/Grid";
import Paper from "@mui/material/Paper";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { useTranslation } from "react-i18next";
import {
  ApiError,
  apiPath,
  eventBus,
  features,
  platformFetch,
  readProblem,
  Slot,
  unwrap,
  unwrapEmpty,
  useFeature,
  useFormatters,
  useResource,
  wellKnownCustomFields,
} from "@acme/web-core";
import { ConfirmDialog, CustomFieldsSummary, MoneyText, PageHeader, ProblemAlert } from "@acme/ui-kit";
import { actions } from "../actions";
import { basePath } from "../basePath";
import { requests } from "../client";
import { ApprovalTimeline } from "../components/ApprovalTimeline";
import { StatusBadge } from "../components/StatusBadge";
import type { RequestDto } from "../types";

type Pending = "delete" | "reject" | null;

/** The cost center the tenant chose for this request, if it has that field. Other modules read it through the slot's props. */
function costCenter(request: RequestDto): { costCenter?: string } {
  const value = request.customFields[wellKnownCustomFields.costCenter];
  return value ? { costCenter: value } : {};
}

export function Component() {
  const { t } = useTranslation("purchase-request");
  const { id = "" } = useParams();
  const navigate = useNavigate();
  const format = useFormatters();
  const canShare = useFeature(features.purchaseRequestDraftSharing);
  const detail = useResource(() => unwrap(requests.GET("/api/purchase-request/v1/requests/{id}", { params: { path: { id } } })), [id]);
  const [pending, setPending] = useState<Pending>(null);
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<Error | undefined>(undefined);

  const request = detail.data;
  const allowed = new Set(request?.allowedActions ?? []);
  const ifMatch = (r: RequestDto) => ({ "If-Match": r.eTag });

  async function run(change: () => Promise<unknown>, after?: () => void) {
    setBusy(true);
    setError(undefined);
    try {
      await change();
      eventBus.emit("purchase-request.changed", { requestId: id });
      if (after) after();
      else await detail.reload();
    } catch (e) {
      setError(e as Error);
      await detail.reload();
    } finally {
      setBusy(false);
      setPending(null);
    }
  }

  async function uploadQuote(file: File) {
    if (!request) return;
    await run(async () => {
      const body = new FormData();
      body.append("file", file);
      const response = await platformFetch(
        new Request(new URL(apiPath("purchase-request", `v1/requests/${id}/quote`), window.location.origin), {
          method: "POST",
          body,
          headers: ifMatch(request),
        }),
      );
      if (!response.ok) throw await readProblem(response);
    });
  }

  /** Asks the server for the draft's link (404 unless the tenant has the feature) and puts it on the clipboard. */
  async function shareDraft() {
    try {
      const link = await unwrap(requests.POST("/api/purchase-request/v1/requests/{id}/share-draft", { params: { path: { id } } }));
      await navigator.clipboard.writeText(new URL(link.url, window.location.origin).toString());
      eventBus.emit("notify", { message: t("detail.linkCopied"), severity: "success" });
    } catch (e) {
      setError(e as Error);
    }
  }

  async function openQuote() {
    const link = await unwrap(requests.GET("/api/purchase-request/v1/requests/{id}/quote", { params: { path: { id } } }));
    window.open(link.url, "_blank", "noopener");
  }

  if (detail.error && !request) return <ProblemAlert error={detail.error} />;
  if (!request) return null;

  return (
    <>
      <PageHeader
        title={`${request.number} · ${request.title}`}
        subtitle={format.date(request.businessDate)}
        actions={
          <>
            {allowed.has(actions.edit) ? (
              <Button component={RouterLink} to="edit" variant="outlined">
                {t("detail.edit")}
              </Button>
            ) : null}
            {allowed.has(actions.submit) ? (
              <Button
                variant="contained"
                disabled={busy}
                onClick={() =>
                  void run(() =>
                    unwrap(
                      requests.POST("/api/purchase-request/v1/requests/{id}/submit", {
                        params: { path: { id }, header: ifMatch(request) },
                      }),
                    ),
                  )
                }
              >
                {t("detail.submit")}
              </Button>
            ) : null}
            {allowed.has(actions.approve) ? (
              <Button
                variant="contained"
                color="success"
                disabled={busy}
                onClick={() =>
                  void run(() =>
                    unwrap(
                      requests.POST("/api/purchase-request/v1/requests/{id}/approve", {
                        params: { path: { id }, header: ifMatch(request) },
                      }),
                    ),
                  )
                }
              >
                {t("detail.approve")}
              </Button>
            ) : null}
            {allowed.has(actions.reject) ? (
              <Button color="error" variant="outlined" disabled={busy} onClick={() => setPending("reject")}>
                {t("detail.reject")}
              </Button>
            ) : null}
            {allowed.has(actions.delete) ? (
              <Button color="error" disabled={busy} onClick={() => setPending("delete")}>
                {t("detail.delete")}
              </Button>
            ) : null}
            {canShare && allowed.has(actions.edit) ? (
              <Button disabled={busy} onClick={() => void shareDraft()}>
                {t("detail.shareDraft")}
              </Button>
            ) : null}
          </>
        }
      />
      <Stack spacing={2}>
        <ProblemAlert error={error} />
        <Grid container spacing={3}>
          <Grid size={{ xs: 12, md: 8 }}>
            <Paper variant="outlined" sx={{ p: 2 }}>
              <Stack direction="row" spacing={2} sx={{ alignItems: "center", mb: 1 }}>
                <StatusBadge status={request.status} />
                <Typography variant="h3" component="p">
                  <MoneyText amount={request.total} currency={request.currency} />
                </Typography>
              </Stack>
              {request.description ? <Typography sx={{ mb: 2 }}>{request.description}</Typography> : null}
              <CustomFieldsSummary entity="purchase-request" values={request.customFields} />
              <Table size="small" aria-label={t("fields.lines")}>
                <TableHead>
                  <TableRow>
                    <TableCell>{t("fields.item")}</TableCell>
                    <TableCell align="right">{t("fields.quantity")}</TableCell>
                    <TableCell align="right">{t("fields.unitPrice")}</TableCell>
                    <TableCell align="right">{t("fields.lineTotal")}</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {request.lines.map((line, index) => (
                    <TableRow key={index}>
                      <TableCell>{line.item}</TableCell>
                      <TableCell align="right">{format.number(line.quantity)}</TableCell>
                      <TableCell align="right">
                        <MoneyText amount={line.unitPrice} currency={request.currency} />
                      </TableCell>
                      <TableCell align="right">
                        <MoneyText amount={line.lineTotal} currency={request.currency} />
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
              <Stack direction="row" spacing={1} sx={{ mt: 2, alignItems: "center" }}>
                <Typography variant="body2">{t("detail.quote")}:</Typography>
                {request.quoteFileId ? (
                  <Button size="small" onClick={() => void openQuote()}>
                    {t("detail.downloadQuote")}
                  </Button>
                ) : (
                  <Typography variant="body2" color="text.secondary">
                    {t("detail.noQuote")}
                  </Typography>
                )}
                {allowed.has(actions.uploadQuote) ? (
                  <Button size="small" component="label" disabled={busy}>
                    {request.quoteFileId ? t("detail.replaceQuote") : t("detail.uploadQuote")}
                    <input
                      hidden
                      type="file"
                      accept=".pdf,.png,.jpg,.jpeg,.docx,.xlsx"
                      onChange={(e) => {
                        const file = e.target.files?.[0];
                        if (file) void uploadQuote(file);
                        e.target.value = "";
                      }}
                    />
                  </Button>
                ) : null}
              </Stack>
            </Paper>
          </Grid>
          <Grid size={{ xs: 12, md: 4 }}>
            <Stack spacing={2}>
              <Paper variant="outlined" sx={{ p: 2 }}>
                <Typography variant="h3" component="h2" sx={{ mb: 1 }}>
                  {t("timeline.title")}
                </Typography>
                <ApprovalTimeline request={request} />
              </Paper>
              {/* Other modules may add a panel here (for example the remaining budget). With nothing registered it renders nothing. */}
              <Slot
                name="purchase-request.detail.side-panel"
                props={{
                  requestId: request.id,
                  total: { amount: Number(request.total), currency: request.currency },
                  ...costCenter(request),
                }}
              />
            </Stack>
          </Grid>
        </Grid>
      </Stack>

      <ConfirmDialog
        open={pending === "delete"}
        title={t("detail.deleteTitle")}
        text={t("detail.deleteText", { number: request.number })}
        confirmLabel={t("detail.delete")}
        busy={busy}
        onCancel={() => setPending(null)}
        onConfirm={() =>
          void run(
            () =>
              unwrapEmpty(
                requests.DELETE("/api/purchase-request/v1/requests/{id}", { params: { path: { id }, header: ifMatch(request) } }),
              ),
            () => void navigate(basePath),
          )
        }
      />
      <Dialog open={pending === "reject"} onClose={() => setPending(null)} aria-labelledby="reject-title" fullWidth maxWidth="sm">
        <DialogTitle id="reject-title">{t("detail.rejectTitle")}</DialogTitle>
        <DialogContent>
          <TextField
            autoFocus
            multiline
            minRows={2}
            label={t("fields.reason")}
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            error={error instanceof ApiError && error.forField("reason") !== undefined}
            sx={{ mt: 1 }}
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setPending(null)}>{t("form.cancel")}</Button>
          <Button
            color="error"
            variant="contained"
            disabled={busy || reason.trim() === ""}
            onClick={() =>
              void run(() =>
                unwrap(
                  requests.POST("/api/purchase-request/v1/requests/{id}/reject", {
                    params: { path: { id }, header: ifMatch(request) },
                    body: { reason },
                  }),
                ),
              )
            }
          >
            {t("detail.reject")}
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
