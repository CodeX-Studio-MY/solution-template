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

export type PrincipalKind = "machine" | "service";

/**
 * The shared page of devices (`machine`) and applications (`service`): the two kinds of non-user principal. Both hold module roles
 * directly and through groups. A device gets a one-time code to enroll with and registers only its public key; an application is
 * given a client ID and a secret, shown once.
 */
export function PrincipalListPage({ kind }: { kind: PrincipalKind }) {
  const { t } = useTranslation("settings");
  const ns = kind === "machine" ? "devices" : "applications";
  const canEdit = useCanManageAccess();
  const dialog = useDialog();
  const format = useFormatters();
  const [adding, setAdding] = useState(false);
  const [openId, setOpenId] = useState<string | null>(null);
  const list = useApiQuery(["identity", "principals", kind], (signal) =>
    unwrap(identity.GET("/api/identity/v1/principals", { params: { query: { type: kind } }, signal })),
  );

  async function showCode(name: string, enrollment: Enrollment) {
    await dialog.alert({
      title: t("devices.codeTitle", { name }),
      text: t("devices.codeText", { code: enrollment.code, expires: format.dateTime(enrollment.expiresOn) }),
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
        title={t(`sections.${ns}`)}
        subtitle={t(`${ns}.subtitle`)}
        actions={
          canEdit ? (
            <Button variant="contained" onClick={() => setAdding(true)}>
              {t(`${ns}.add`)}
            </Button>
          ) : null
        }
      />
      <ProblemAlert error={list.error ?? undefined} />
      {list.data && rows.length === 0 ? (
        <EmptyState title={t(`${ns}.empty`)} />
      ) : (
        <Paper variant="outlined" sx={{ overflowX: "auto" }}>
          <Table size="small" aria-label={t(`sections.${ns}`)}>
            <TableHead>
              <TableRow>
                <TableCell>{t("principals.name")}</TableCell>
                <TableCell>{t("principals.clientId")}</TableCell>
                <TableCell>{t("principals.status")}</TableCell>
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
                  <TableCell sx={{ fontFamily: "monospace" }}>{principal.clientId ?? "—"}</TableCell>
                  <TableCell>
                    <StatusChip label={t(`common:status.${principal.status}`)} tone={principal.isActive ? "success" : "default"} />
                  </TableCell>
                  <TableCell align="right">
                    {canEdit && kind === "machine" ? (
                      <Button size="small" onClick={() => newCode.mutate(principal)} disabled={newCode.isPending}>
                        {t("devices.newCode")}
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
      {adding && kind === "machine" ? <AddDeviceDialog onClose={() => setAdding(false)} onCreated={showCode} /> : null}
      {adding && kind === "service" ? <AddApplicationDialog onClose={() => setAdding(false)} /> : null}
    </>
  );
}

function AddDeviceDialog({
  onClose,
  onCreated,
}: {
  onClose: () => void;
  onCreated: (name: string, enrollment: Enrollment) => Promise<void>;
}) {
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
      title={t("devices.addTitle")}
      submitLabel={t("common:actions.create")}
      busy={create.isPending}
      onSubmit={() => create.mutate()}
      onClose={onClose}
    >
      <TextField
        label={t("principals.name")}
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

/** 32 random bytes, base64url: the application's client secret. Made here, sent once, shown once; the server keeps only a hash. */
function newSecret(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(32));
  return btoa(String.fromCharCode(...bytes))
    .replace(/\+/g, "-")
    .replace(/\//g, "_")
    .replace(/=+$/, "");
}

function AddApplicationDialog({ onClose }: { onClose: () => void }) {
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
          title: t("applications.secretTitle", { name: service.displayName }),
          text: t("applications.secretText", { clientId: service.clientId ?? "", secret }),
          confirmLabel: t("common:actions.close"),
        });
      },
    },
  );
  const error = create.error instanceof ApiError ? create.error.forField("displayName")?.message : undefined;
  return (
    <FormDialog
      open
      title={t("applications.addTitle")}
      text={t("applications.addText")}
      submitLabel={t("common:actions.create")}
      busy={create.isPending}
      onSubmit={() => create.mutate()}
      onClose={onClose}
    >
      <TextField
        label={t("principals.name")}
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

/** What a device or application may do, per module: the roles it holds directly (groups may give more). */
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
        {t(`principals.types.${principal.type}`)} · {principal.clientId ?? "—"}
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
