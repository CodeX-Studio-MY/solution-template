import { useState } from "react";
import Button from "@mui/material/Button";
import Paper from "@mui/material/Paper";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { useTranslation } from "react-i18next";
import { ApiError, unwrap, unwrapEmpty, useApiMutation, useApiQuery, useDialog, useFormatters, type Role } from "@saaskit/web-core";
import { EmptyState, FormDialog, PageHeader, ProblemAlert, StatusChip } from "@saaskit/ui-kit";
import { identity, identityQueries, useCanManageAccess, type Enrollment, type Principal } from "../api";
import { SidePanel } from "../SidePanel";
import { RolesEditor } from "./RolesEditor";

/** Shared devices and integrations. A device gets a one-time code to enroll with; it registers only its public key. */
export function Component() {
  const { t } = useTranslation("settings");
  const canEdit = useCanManageAccess();
  const dialog = useDialog();
  const format = useFormatters();
  const [adding, setAdding] = useState<"machine" | "service" | null>(null);
  const [openId, setOpenId] = useState<string | null>(null);
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
            <>
              <Button variant="outlined" onClick={() => setAdding("service")}>
                {t("machines.addIntegration")}
              </Button>
              <Button variant="contained" onClick={() => setAdding("machine")}>
                {t("machines.add")}
              </Button>
            </>
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
                  <TableCell>
                    <Button variant="text" onClick={() => setOpenId(principal.id)} sx={{ p: 0, minWidth: 0, textTransform: "none" }}>
                      {principal.displayName}
                    </Button>
                  </TableCell>
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
      <SidePanel open={Boolean(rows.find((p) => p.id === openId))} onClose={() => setOpenId(null)}>
        {rows.find((p) => p.id === openId) ? <AccessPanel principal={rows.find((p) => p.id === openId)!} canEdit={canEdit} /> : null}
      </SidePanel>
      {adding === "machine" ? <AddDialog onClose={() => setAdding(null)} onCreated={showCode} /> : null}
      {adding === "service" ? <AddIntegrationDialog onClose={() => setAdding(null)} /> : null}
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

/** 32 random bytes, base64url: the integration's client secret. Made here, sent once, shown once; the server keeps only a hash. */
function newSecret(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(32));
  return btoa(String.fromCharCode(...bytes))
    .replace(/\+/g, "-")
    .replace(/\//g, "_")
    .replace(/=+$/, "");
}

function AddIntegrationDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation("settings");
  const dialog = useDialog();
  const [name, setName] = useState("");
  const create = useApiMutation(
    async () => {
      const secret = newSecret();
      const service = await unwrap(identity.POST("/api/identity/v1/services", { body: { displayName: name, secret, publicKeyJwk: null } }));
      return { service, secret };
    },
    {
      invalidate: identityQueries,
      onSuccess: async ({ service, secret }) => {
        onClose();
        await dialog.alert({
          title: t("machines.secretTitle", { name: service.displayName }),
          text: t("machines.secretText", { clientId: service.clientId ?? "", secret }),
          confirmLabel: t("common:actions.close"),
        });
      },
    },
  );
  const error = create.error instanceof ApiError ? create.error.forField("displayName")?.message : undefined;
  return (
    <FormDialog
      open
      title={t("machines.integrationTitle")}
      text={t("machines.integrationText")}
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

/** What a device or integration may do, per application. */
function AccessPanel({ principal, canEdit }: { principal: Principal; canEdit: boolean }) {
  const { t } = useTranslation("settings");
  const id = principal.id;
  const assignments = useApiQuery(["identity", "principals", id, "assignments"], (signal) =>
    unwrap(identity.GET("/api/identity/v1/principals/{id}/assignments", { params: { path: { id } }, signal })),
  );
  const setRole = useApiMutation(
    async ({ module, role }: { module: string; role: Role | null }) => {
      if (role)
        await unwrap(
          identity.PUT("/api/identity/v1/principals/{id}/assignments/{module}", { params: { path: { id, module } }, body: { role } }),
        );
      else
        await unwrapEmpty(identity.DELETE("/api/identity/v1/principals/{id}/assignments/{module}", { params: { path: { id, module } } }));
    },
    { invalidate: identityQueries, success: t("users.roleSaved") },
  );
  return (
    <Stack spacing={3}>
      <Typography variant="h2" component="h2">
        {principal.displayName}
      </Typography>
      <Typography variant="body2" color="text.secondary">
        {t(`machines.types.${principal.type}`)} · {principal.clientId ?? "—"}
      </Typography>
      <ProblemAlert error={assignments.error ?? undefined} />
      <Typography variant="h3" component="h3">
        {t("users.roles")}
      </Typography>
      <RolesEditor
        value={Object.fromEntries((assignments.data ?? []).map((a) => [a.module, a.role]))}
        onChange={(module, role) => setRole.mutate({ module, role })}
        disabled={!canEdit || setRole.isPending}
      />
    </Stack>
  );
}
