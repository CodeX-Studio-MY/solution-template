import { useState } from "react";
import Button from "@mui/material/Button";
import Paper from "@mui/material/Paper";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import { useTranslation } from "react-i18next";
import { ApiError, unwrap, useApiMutation, useApiQuery, useDialog, useFormatters } from "@saaskit/web-core";
import { EmptyState, FormDialog, PageHeader, ProblemAlert, StatusChip } from "@saaskit/ui-kit";
import { identity, identityQueries, useCanManageAccess, type Enrollment, type Principal } from "../api";

/** Shared devices and integrations. A device gets a one-time code to enroll with; it registers only its public key. */
export function Component() {
  const { t } = useTranslation("settings");
  const canEdit = useCanManageAccess();
  const dialog = useDialog();
  const format = useFormatters();
  const [adding, setAdding] = useState(false);
  const list = useApiQuery(["identity", "principals", "non-users"], async (signal) => {
    const [machines, services] = await Promise.all([
      unwrap(identity.GET("/api/identity/v1/principals", { params: { query: { type: "machine" } }, signal })),
      unwrap(identity.GET("/api/identity/v1/principals", { params: { query: { type: "service" } }, signal })),
    ]);
    return [...machines, ...services];
  });

  async function showCode(name: string, enrollment: Enrollment) {
    await dialog.alert({
      title: t("machines.codeTitle", { name }),
      text: t("machines.codeText", { code: enrollment.code, expires: format.dateTime(enrollment.expiresOn) }),
      confirmLabel: t("common:actions.close"),
    });
  }

  const newCode = useApiMutation(
    (machine: Principal) => unwrap(identity.POST("/api/identity/v1/machines/{id}/enrollment", { params: { path: { id: machine.id } } })),
    { onSuccess: (enrollment, machine) => showCode(machine.displayName, enrollment) },
  );

  const rows = list.data ?? [];
  return (
    <>
      <PageHeader
        title={t("sections.machines")}
        subtitle={t("machines.subtitle")}
        actions={
          canEdit ? (
            <Button variant="contained" onClick={() => setAdding(true)}>
              {t("machines.add")}
            </Button>
          ) : null
        }
      />
      <ProblemAlert error={list.error ?? undefined} />
      {list.data && rows.length === 0 ? (
        <EmptyState title={t("machines.empty")} />
      ) : (
        <Paper variant="outlined" sx={{ overflowX: "auto" }}>
          <Table size="small" aria-label={t("sections.machines")}>
            <TableHead>
              <TableRow>
                <TableCell>{t("machines.name")}</TableCell>
                <TableCell>{t("machines.type")}</TableCell>
                <TableCell>{t("machines.clientId")}</TableCell>
                <TableCell>{t("machines.status")}</TableCell>
                <TableCell />
              </TableRow>
            </TableHead>
            <TableBody>
              {rows.map((principal) => (
                <TableRow key={principal.id}>
                  <TableCell>{principal.displayName}</TableCell>
                  <TableCell>{t(`machines.types.${principal.type}`)}</TableCell>
                  <TableCell sx={{ fontFamily: "monospace" }}>{principal.clientId ?? "—"}</TableCell>
                  <TableCell>
                    <StatusChip label={t(`common:status.${principal.status}`)} tone={principal.isActive ? "success" : "default"} />
                  </TableCell>
                  <TableCell align="right">
                    {canEdit && principal.type === "machine" ? (
                      <Button size="small" onClick={() => newCode.mutate(principal)} disabled={newCode.isPending}>
                        {t("machines.newCode")}
                      </Button>
                    ) : null}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Paper>
      )}
      {adding ? <AddDialog onClose={() => setAdding(false)} onCreated={showCode} /> : null}
    </>
  );
}

function AddDialog({ onClose, onCreated }: { onClose: () => void; onCreated: (name: string, enrollment: Enrollment) => Promise<void> }) {
  const { t } = useTranslation("settings");
  const [name, setName] = useState("");
  const create = useApiMutation(() => unwrap(identity.POST("/api/identity/v1/machines", { body: { displayName: name } })), {
    invalidate: identityQueries,
    onSuccess: (enrollment) => {
      onClose();
      return onCreated(name, enrollment);
    },
  });
  const error = create.error instanceof ApiError ? create.error.forField("displayName")?.message : undefined;
  return (
    <FormDialog
      open
      title={t("machines.addTitle")}
      submitLabel={t("common:actions.create")}
      busy={create.isPending}
      onSubmit={() => create.mutate()}
      onClose={onClose}
    >
      <TextField
        label={t("machines.name")}
        required
        autoFocus
        value={name}
        onChange={(e) => setName(e.target.value)}
        error={Boolean(error)}
        helperText={error}
      />
    </FormDialog>
  );
}
