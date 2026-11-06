import Button from "@mui/material/Button";
import Paper from "@mui/material/Paper";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import { useTranslation } from "react-i18next";
import { unwrap, useApiMutation, useApiQuery, useDialog, useFormatters } from "@saaskit/web-core";
import { EmptyState, PageHeader, ProblemAlert, StatusChip, type Tone } from "@saaskit/ui-kit";
import { tenancy, tenancyQueries, useModuleName, type SupportAccess } from "../api";

const decisions = {
  approve: (id: string) => tenancy.POST("/api/tenancy/v1/support-access/{id}/approve", { params: { path: { id } } }),
  deny: (id: string) => tenancy.POST("/api/tenancy/v1/support-access/{id}/deny", { params: { path: { id } } }),
  revoke: (id: string) => tenancy.POST("/api/tenancy/v1/support-access/{id}/revoke", { params: { path: { id } } }),
};
type Decision = keyof typeof decisions;
const done: Record<Decision, string> = { approve: "support.approved", deny: "support.denied", revoke: "support.revoked" };

const tones: Record<SupportAccess["status"], Tone> = { pending: "warning", approved: "success", denied: "default", revoked: "default" };

/** Support requests from the provider: approve, deny or revoke. Unless the tenant waived approval, nothing is granted without an admin's yes. */
export function Component() {
  const { t } = useTranslation("settings");
  const dialog = useDialog();
  const format = useFormatters();
  const moduleName = useModuleName();
  const list = useApiQuery(["tenancy", "support-access"], (signal) => unwrap(tenancy.GET("/api/tenancy/v1/support-access", { signal })));
  const decide = useApiMutation(({ id, decision }: { id: string; decision: Decision }) => unwrap(decisions[decision](id)), {
    invalidate: tenancyQueries,
    success: (_, { decision }) => t(done[decision]),
  });

  async function ask(request: SupportAccess, decision: Decision) {
    const module = moduleName(request.module);
    const confirmed = await dialog.confirm({
      title: t(`support.${decision}Title`),
      text:
        decision === "approve"
          ? t("support.approveText", {
              operator: request.operatorName,
              role: t(`common:roles.${request.role}`),
              module,
              expires: format.dateTime(request.expiresOn),
            })
          : request.reason,
      confirmLabel: t(`support.${decision}`),
      tone: decision === "approve" ? "default" : "danger",
    });
    if (confirmed) decide.mutate({ id: request.id, decision });
  }

  const rows = list.data ?? [];
  return (
    <>
      <PageHeader title={t("sections.support")} subtitle={t("support.subtitle")} />
      <ProblemAlert error={list.error ?? undefined} />
      {list.data && rows.length === 0 ? (
        <EmptyState title={t("support.empty")} />
      ) : (
        <Paper variant="outlined" sx={{ overflowX: "auto" }}>
          <Table size="small" aria-label={t("sections.support")}>
            <TableHead>
              <TableRow>
                <TableCell>{t("support.operator")}</TableCell>
                <TableCell>{t("support.module")}</TableCell>
                <TableCell>{t("support.role")}</TableCell>
                <TableCell>{t("support.reason")}</TableCell>
                <TableCell>{t("support.expires")}</TableCell>
                <TableCell>{t("support.status")}</TableCell>
                <TableCell />
              </TableRow>
            </TableHead>
            <TableBody>
              {rows.map((request) => (
                <TableRow key={request.id}>
                  <TableCell>{request.operatorName}</TableCell>
                  <TableCell>{moduleName(request.module)}</TableCell>
                  <TableCell>{t(`common:roles.${request.role}`)}</TableCell>
                  <TableCell>{request.reason}</TableCell>
                  <TableCell>{format.dateTime(request.expiresOn)}</TableCell>
                  <TableCell>
                    <StatusChip label={t(`support.statuses.${request.status}`)} tone={tones[request.status]} />
                  </TableCell>
                  <TableCell align="right">
                    <Stack direction="row" spacing={1} sx={{ justifyContent: "flex-end" }}>
                      {request.status === "pending" ? (
                        <>
                          <Button size="small" variant="contained" onClick={() => void ask(request, "approve")}>
                            {t("support.approve")}
                          </Button>
                          <Button size="small" onClick={() => void ask(request, "deny")}>
                            {t("support.deny")}
                          </Button>
                        </>
                      ) : null}
                      {request.status === "approved" ? (
                        <Button size="small" color="error" onClick={() => void ask(request, "revoke")}>
                          {t("support.revoke")}
                        </Button>
                      ) : null}
                    </Stack>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Paper>
      )}
    </>
  );
}
