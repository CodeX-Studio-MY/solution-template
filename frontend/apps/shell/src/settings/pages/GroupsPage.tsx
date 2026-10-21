import { useState } from "react";
import Button from "@mui/material/Button";
import Divider from "@mui/material/Divider";
import IconButton from "@mui/material/IconButton";
import List from "@mui/material/List";
import ListItem from "@mui/material/ListItem";
import ListItemText from "@mui/material/ListItemText";
import Paper from "@mui/material/Paper";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import Close from "@mui/icons-material/Close";
import { useTranslation } from "react-i18next";
import { ApiError, unwrap, unwrapEmpty, useApiMutation, useApiQuery, useDialog, useMaster, type Role } from "@saaskit/web-core";
import { EmptyState, FormDialog, MasterSelect, PageHeader, ProblemAlert } from "@saaskit/ui-kit";
import { identity, identityQueries, useCanManageAccess, type Group } from "../api";
import { SidePanel } from "../SidePanel";
import { RolesEditor } from "./RolesEditor";

/** Groups: the same roles for several people. A member's access is the highest of their own roles and their groups'. */
export function Component() {
  const { t } = useTranslation("settings");
  const canEdit = useCanManageAccess();
  const [naming, setNaming] = useState<Group | "new" | null>(null);
  const [openId, setOpenId] = useState<string | null>(null);
  const groups = useApiQuery(["identity", "groups"], (signal) => unwrap(identity.GET("/api/identity/v1/groups", { signal })));
  const open = groups.data?.find((g) => g.id === openId);

  return (
    <>
      <PageHeader
        title={t("sections.groups")}
        subtitle={t("groups.subtitle")}
        actions={
          canEdit ? (
            <Button variant="contained" onClick={() => setNaming("new")}>
              {t("groups.create")}
            </Button>
          ) : null
        }
      />
      <ProblemAlert error={groups.error ?? undefined} />
      {groups.data && groups.data.length === 0 ? (
        <EmptyState title={t("groups.empty")} hint={t("groups.emptyHint")} />
      ) : (
        <Paper variant="outlined" sx={{ overflowX: "auto" }}>
          <Table size="small" aria-label={t("sections.groups")}>
            <TableHead>
              <TableRow>
                <TableCell>{t("groups.name")}</TableCell>
                <TableCell align="right">{t("groups.members")}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {(groups.data ?? []).map((group) => (
                <TableRow key={group.id} hover onClick={() => setOpenId(group.id)} sx={{ cursor: "pointer" }}>
                  <TableCell>
                    <Button variant="text" onClick={() => setOpenId(group.id)} sx={{ p: 0, minWidth: 0, textTransform: "none" }}>
                      {group.name}
                    </Button>
                  </TableCell>
                  <TableCell align="right">{group.memberCount}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Paper>
      )}
      {naming ? <NameDialog group={naming === "new" ? null : naming} onClose={() => setNaming(null)} onCreated={setOpenId} /> : null}
      <SidePanel open={Boolean(open)} onClose={() => setOpenId(null)}>
        {open ? <GroupPanel group={open} canEdit={canEdit} onRename={() => setNaming(open)} onDeleted={() => setOpenId(null)} /> : null}
      </SidePanel>
    </>
  );
}

function NameDialog({ group, onClose, onCreated }: { group: Group | null; onClose: () => void; onCreated: (id: string) => void }) {
  const { t } = useTranslation("settings");
  const [name, setName] = useState(group?.name ?? "");
  const save = useApiMutation(
    () =>
      group
        ? unwrap(
            identity.PUT("/api/identity/v1/groups/{id}", {
              params: { path: { id: group.id }, header: { "If-Match": group.eTag } },
              body: { displayName: name },
            }),
          )
        : unwrap(identity.POST("/api/identity/v1/groups", { body: { displayName: name } })),
    {
      invalidate: identityQueries,
      success: group ? t("groups.renamed") : t("groups.created"),
      onSuccess: (saved) => {
        onClose();
        if (!group) onCreated(saved.id);
      },
    },
  );
  const error = save.error instanceof ApiError ? save.error.forField("displayName")?.message : undefined;
  return (
    <FormDialog
      open
      title={group ? t("groups.renameTitle") : t("groups.createTitle")}
      busy={save.isPending}
      onSubmit={() => save.mutate()}
      onClose={onClose}
    >
      <TextField
        label={t("groups.name")}
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

function GroupPanel({
  group,
  canEdit,
  onRename,
  onDeleted,
}: {
  group: Group;
  canEdit: boolean;
  onRename: () => void;
  onDeleted: () => void;
}) {
  const { t } = useTranslation("settings");
  const dialog = useDialog();
  const [adding, setAdding] = useState<string | null>(null);
  const id = group.id;
  const members = useApiQuery(["identity", "groups", id, "members"], (signal) =>
    unwrap(identity.GET("/api/identity/v1/groups/{id}/members", { params: { path: { id } }, signal })),
  );
  const assignments = useApiQuery(["identity", "groups", id, "assignments"], (signal) =>
    unwrap(identity.GET("/api/identity/v1/groups/{id}/assignments", { params: { path: { id } }, signal })),
  );
  // The people to choose from: master data, shared with any other picker of users in the app.
  const users = useMaster("identity", "/api/identity/v1/principals", { query: { type: "user" } });
  const memberIds = new Set((members.data ?? []).map((m) => m.id));
  const candidates = { ...users, options: users.options.filter((o) => !memberIds.has(o.value)) };

  const addMember = useApiMutation(
    (principalId: string) =>
      unwrapEmpty(identity.PUT("/api/identity/v1/groups/{id}/members/{principalId}", { params: { path: { id, principalId } } })),
    { invalidate: identityQueries, success: t("groups.memberAdded"), onSuccess: () => setAdding(null) },
  );
  const removeMember = useApiMutation(
    (principalId: string) =>
      unwrapEmpty(identity.DELETE("/api/identity/v1/groups/{id}/members/{principalId}", { params: { path: { id, principalId } } })),
    { invalidate: identityQueries, success: t("groups.memberRemoved") },
  );
  const setRole = useApiMutation(
    async ({ module, role }: { module: string; role: Role | null }) => {
      if (role)
        await unwrap(
          identity.PUT("/api/identity/v1/groups/{id}/assignments/{module}", { params: { path: { id, module } }, body: { role } }),
        );
      else await unwrapEmpty(identity.DELETE("/api/identity/v1/groups/{id}/assignments/{module}", { params: { path: { id, module } } }));
    },
    { invalidate: identityQueries, success: t("users.roleSaved") },
  );
  const remove = useApiMutation(() => unwrapEmpty(identity.DELETE("/api/identity/v1/groups/{id}", { params: { path: { id } } })), {
    invalidate: identityQueries,
    success: t("groups.deleted"),
    onSuccess: onDeleted,
  });

  async function confirmDelete() {
    if (
      await dialog.confirm({
        title: t("groups.deleteTitle", { name: group.name }),
        text: t("groups.deleteText"),
        confirmLabel: t("common:actions.delete"),
        tone: "danger",
      })
    )
      remove.mutate();
  }

  const roles = Object.fromEntries((assignments.data ?? []).map((a) => [a.module, a.role]));
  return (
    <Stack spacing={3}>
      <Stack direction="row" sx={{ alignItems: "center", justifyContent: "space-between" }}>
        <Typography variant="h2" component="h2">
          {group.name}
        </Typography>
        {canEdit ? (
          <Stack direction="row" spacing={1}>
            <Button onClick={onRename}>{t("common:actions.edit")}</Button>
            <Button color="error" onClick={() => void confirmDelete()}>
              {t("common:actions.delete")}
            </Button>
          </Stack>
        ) : null}
      </Stack>
      <ProblemAlert error={members.error ?? assignments.error ?? undefined} />
      <Typography variant="h3" component="h3">
        {t("groups.members")}
      </Typography>
      {canEdit ? (
        <Stack direction="row" spacing={1} sx={{ alignItems: "flex-start" }}>
          <div style={{ flexGrow: 1 }}>
            <MasterSelect master={candidates} label={t("groups.addMember")} value={adding} onChange={setAdding} />
          </div>
          <Button variant="outlined" disabled={!adding || addMember.isPending} onClick={() => adding && addMember.mutate(adding)}>
            {t("common:actions.add")}
          </Button>
        </Stack>
      ) : null}
      {members.data?.length === 0 ? (
        <Typography color="text.secondary">{t("groups.noMembers")}</Typography>
      ) : (
        <List dense disablePadding>
          {(members.data ?? []).map((member) => (
            <ListItem
              key={member.id}
              disableGutters
              secondaryAction={
                canEdit ? (
                  <IconButton
                    edge="end"
                    aria-label={t("groups.removeMember", { name: member.displayName })}
                    onClick={() => removeMember.mutate(member.id)}
                  >
                    <Close />
                  </IconButton>
                ) : null
              }
            >
              <ListItemText primary={member.displayName} secondary={member.email} />
            </ListItem>
          ))}
        </List>
      )}
      <Divider />
      <Typography variant="h3" component="h3">
        {t("groups.roles")}
      </Typography>
      <RolesEditor value={roles} onChange={(module, role) => setRole.mutate({ module, role })} disabled={!canEdit || setRole.isPending} />
    </Stack>
  );
}
