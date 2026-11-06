import { useMemo, useState } from "react";
import Button from "@mui/material/Button";
import Divider from "@mui/material/Divider";
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
import { ApiError, unwrap, unwrapEmpty, useApiMutation, useApiQuery, useDialog, useFormatters, type Role } from "@saaskit/web-core";
import { EmptyState, FormDialog, PageHeader, ProblemAlert, StatusChip, type Tone } from "@saaskit/ui-kit";
import { identity, identityQueries, useCanManageAccess, useModuleName, type Principal } from "../api";
import { SidePanel } from "../SidePanel";
import { RolesEditor } from "./RolesEditor";

const tones: Record<Principal["status"], Tone> = { active: "success", inactive: "default", invited: "info" };

/** People: who can sign in, invite more, and what each can do in each application. */
export function Component() {
  const { t } = useTranslation("settings");
  const format = useFormatters();
  const canEdit = useCanManageAccess();
  const [search, setSearch] = useState("");
  const [inviting, setInviting] = useState(false);
  const [openId, setOpenId] = useState<string | null>(null);
  const users = useApiQuery(["identity", "principals", "user"], (signal) =>
    unwrap(identity.GET("/api/identity/v1/principals", { params: { query: { type: "user" } }, signal })),
  );
  const shown = useMemo(() => {
    const term = search.trim().toLowerCase();
    return (users.data ?? []).filter(
      (u) => !term || u.displayName.toLowerCase().includes(term) || (u.email ?? "").toLowerCase().includes(term),
    );
  }, [users.data, search]);
  const open = users.data?.find((u) => u.id === openId);

  return (
    <>
      <PageHeader
        title={t("sections.users")}
        subtitle={t("users.subtitle")}
        actions={
          canEdit ? (
            <Button variant="contained" onClick={() => setInviting(true)}>
              {t("users.invite")}
            </Button>
          ) : null
        }
      />
      <ProblemAlert error={users.error ?? undefined} />
      {users.data && users.data.length === 0 ? (
        <EmptyState title={t("users.empty")} hint={t("users.emptyHint")} />
      ) : (
        <Stack spacing={2}>
          <TextField
            size="small"
            type="search"
            label={t("users.search")}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            sx={{ maxWidth: 320 }}
          />
          <Paper variant="outlined" sx={{ overflowX: "auto" }}>
            <Table size="small" aria-label={t("sections.users")}>
              <TableHead>
                <TableRow>
                  <TableCell>{t("users.name")}</TableCell>
                  <TableCell>{t("users.email")}</TableCell>
                  <TableCell>{t("users.status")}</TableCell>
                  <TableCell>{t("users.lastSeen")}</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {shown.map((user) => (
                  <TableRow key={user.id} hover onClick={() => setOpenId(user.id)} sx={{ cursor: "pointer" }}>
                    <TableCell>
                      <Button variant="text" onClick={() => setOpenId(user.id)} sx={{ p: 0, minWidth: 0, textTransform: "none" }}>
                        {user.displayName}
                      </Button>
                    </TableCell>
                    <TableCell>{user.email}</TableCell>
                    <TableCell>
                      <StatusChip label={t(`common:status.${user.status}`)} tone={tones[user.status]} />
                    </TableCell>
                    <TableCell>{user.lastSeenOn ? format.dateTime(user.lastSeenOn) : t("users.never")}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </Paper>
        </Stack>
      )}
      {inviting ? <InviteDialog onClose={() => setInviting(false)} /> : null}
      <SidePanel open={Boolean(open)} onClose={() => setOpenId(null)}>
        {open ? <UserPanel user={open} canEdit={canEdit} /> : null}
      </SidePanel>
    </>
  );
}

function InviteDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation("settings");
  const [form, setForm] = useState({ email: "", displayName: "" });
  const [roles, setRoles] = useState<Partial<Record<string, Role>>>({});
  const invite = useApiMutation(
    () =>
      unwrap(
        identity.POST("/api/identity/v1/users/invitations", {
          body: { ...form, roles: Object.entries(roles).flatMap(([module, role]) => (role ? [{ module, role }] : [])) },
        }),
      ),
    {
      invalidate: identityQueries,
      success: (sent) => t(sent.emailSent ? "users.invited" : "users.invitedNoEmail", { email: form.email }),
      onSuccess: onClose,
    },
  );
  const fieldError = (name: string) => (invite.error instanceof ApiError ? invite.error.forField(name)?.message : undefined);
  return (
    <FormDialog
      open
      title={t("users.inviteTitle")}
      text={t("users.inviteText")}
      submitLabel={t("users.invite")}
      busy={invite.isPending}
      onSubmit={() => invite.mutate()}
      onClose={onClose}
      maxWidth="sm"
    >
      <TextField
        label={t("users.email")}
        type="email"
        required
        autoFocus
        value={form.email}
        onChange={(e) => setForm((f) => ({ ...f, email: e.target.value }))}
        error={Boolean(fieldError("email"))}
        helperText={fieldError("email")}
      />
      <TextField
        label={t("users.name")}
        required
        value={form.displayName}
        onChange={(e) => setForm((f) => ({ ...f, displayName: e.target.value }))}
        error={Boolean(fieldError("displayName"))}
        helperText={fieldError("displayName")}
      />
      <Typography variant="h4" component="h3">
        {t("users.roles")}
      </Typography>
      <Typography variant="body2" color="text.secondary">
        {t("users.rolesHint")}
      </Typography>
      <RolesEditor value={roles} onChange={(module, role) => setRoles((r) => ({ ...r, [module]: role ?? undefined }))} />
    </FormDialog>
  );
}

/** One user: name, sign-in state, invitation, direct roles and the effective result. */
function UserPanel({ user, canEdit }: { user: Principal; canEdit: boolean }) {
  const { t } = useTranslation("settings");
  const dialog = useDialog();
  const moduleName = useModuleName();
  const [name, setName] = useState(user.displayName);
  const path = { params: { path: { id: user.id } } };
  const assignments = useApiQuery(["identity", "principals", user.id, "assignments"], (signal) =>
    unwrap(identity.GET("/api/identity/v1/principals/{id}/assignments", { ...path, signal })),
  );
  const access = useApiQuery(["identity", "principals", user.id, "access"], (signal) =>
    unwrap(identity.GET("/api/identity/v1/principals/{id}/access", { ...path, signal })),
  );
  const direct = Object.fromEntries((assignments.data ?? []).map((a) => [a.module, a.role]));

  const rename = useApiMutation(
    () =>
      unwrap(
        identity.PUT("/api/identity/v1/principals/{id}", {
          params: { path: { id: user.id }, header: { "If-Match": user.eTag } },
          body: { displayName: name },
        }),
      ),
    { invalidate: identityQueries, success: t("users.saved") },
  );
  const setActive = useApiMutation(
    (active: boolean) =>
      unwrap(
        identity.PUT("/api/identity/v1/principals/{id}/active", {
          params: { path: { id: user.id }, header: { "If-Match": user.eTag } },
          body: { active },
        }),
      ),
    {
      invalidate: identityQueries,
      success: (_, active) => t(active ? "users.activated" : "users.deactivated", { name: user.displayName }),
    },
  );
  const resend = useApiMutation(() => unwrap(identity.POST("/api/identity/v1/users/{id}/invitation", path)), {
    success: (sent) => t(sent.emailSent ? "users.resent" : "users.resentNoEmail"),
  });
  const setRole = useApiMutation(
    async ({ module, role }: { module: string; role: Role | null }) => {
      if (role)
        await unwrap(
          identity.PUT("/api/identity/v1/principals/{id}/assignments/{module}", {
            params: { path: { id: user.id, module } },
            body: { role },
          }),
        );
      else
        await unwrapEmpty(
          identity.DELETE("/api/identity/v1/principals/{id}/assignments/{module}", { params: { path: { id: user.id, module } } }),
        );
    },
    { invalidate: identityQueries, success: t("users.roleSaved") },
  );

  async function deactivate() {
    if (
      await dialog.confirm({
        title: t("users.deactivateTitle", { name: user.displayName }),
        text: t("users.deactivateText"),
        confirmLabel: t("users.deactivate"),
        tone: "danger",
      })
    )
      setActive.mutate(false);
  }

  const renameError = rename.error instanceof ApiError ? rename.error.forField("displayName")?.message : undefined;
  const effective = Object.entries(access.data?.roles ?? {});
  return (
    <Stack spacing={3}>
      <Typography variant="h2" component="h2">
        {t("users.edit")}
      </Typography>
      <ProblemAlert error={assignments.error ?? access.error ?? undefined} />
      <Stack direction="row" spacing={1} sx={{ alignItems: "flex-start" }}>
        <TextField
          fullWidth
          label={t("users.name")}
          value={name}
          onChange={(e) => setName(e.target.value)}
          disabled={!canEdit}
          error={Boolean(renameError)}
          helperText={renameError ?? user.email}
        />
        {canEdit ? (
          <Button onClick={() => rename.mutate()} disabled={rename.isPending || name.trim() === user.displayName}>
            {t("common:actions.save")}
          </Button>
        ) : null}
      </Stack>
      {canEdit ? (
        <Stack direction="row" spacing={1}>
          {user.status === "invited" ? (
            <Button variant="outlined" onClick={() => resend.mutate()} disabled={resend.isPending}>
              {t("users.resend")}
            </Button>
          ) : user.isActive ? (
            <Button color="error" variant="outlined" onClick={() => void deactivate()} disabled={setActive.isPending}>
              {t("users.deactivate")}
            </Button>
          ) : (
            <Button variant="outlined" onClick={() => setActive.mutate(true)} disabled={setActive.isPending}>
              {t("users.activate")}
            </Button>
          )}
        </Stack>
      ) : null}
      <Divider />
      <Stack spacing={1}>
        <Typography variant="h3" component="h3">
          {t("users.roles")}
        </Typography>
        <Typography variant="body2" color="text.secondary">
          {t("users.rolesHint")}
        </Typography>
      </Stack>
      <RolesEditor value={direct} onChange={(module, role) => setRole.mutate({ module, role })} disabled={!canEdit || setRole.isPending} />
      <Divider />
      <Stack spacing={1}>
        <Typography variant="h3" component="h3">
          {t("users.effective")}
        </Typography>
        <Typography variant="body2" color="text.secondary">
          {t("users.effectiveHint")}
        </Typography>
        {effective.length === 0 ? (
          <Typography>{t("users.noAccess")}</Typography>
        ) : (
          <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: "wrap" }}>
            {effective.map(([module, role]) => (
              <StatusChip key={module} label={`${moduleName(module)}: ${t(`common:roles.${role}`)}`} />
            ))}
          </Stack>
        )}
      </Stack>
    </Stack>
  );
}
