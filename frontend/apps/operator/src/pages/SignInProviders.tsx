import { useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import MenuItem from "@mui/material/MenuItem";
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
import { ApiError, snackbar, toNumber, unwrap, unwrapEmpty, useApiMutation, useApiQuery, useDialog } from "@saaskit/web-core";
import { EmptyState, FormDialog, PageHeader, ProblemAlert, StatusChip } from "@saaskit/ui-kit";
import { providersKey, tenancy, type IdentityProviderRow, type LoginProvider } from "../api";
import { Identifier } from "../components/principals";

const kinds: readonly LoginProvider[] = ["google", "microsoft", "apple", "facebook"];

/** One row per set of provider settings: a kind of provider can have several (two Microsoft directories, separate Google apps). */
export function Component() {
  const { t } = useTranslation("operator");
  const dialog = useDialog();
  const [editing, setEditing] = useState<IdentityProviderRow | "new" | null>(null);
  const providers = useApiQuery(providersKey, (signal) => unwrap(tenancy.GET("/api/tenancy/v1/operator/identity-providers", { signal })));

  const setEnabled = useApiMutation(
    ({ row, enabled }: { row: IdentityProviderRow; enabled: boolean }) =>
      unwrap(
        tenancy.PUT("/api/tenancy/v1/operator/identity-providers/{id}/enabled", {
          params: { path: { id: toNumber(row.identityProviderId) }, header: { "If-Match": row.eTag } },
          body: { enabled },
        }),
      ),
    {
      invalidate: [providersKey],
      success: (saved) => t(saved.isEnabled ? "providers.enabledDone" : "providers.disabledDone", { name: saved.displayName }),
    },
  );

  async function toggle(row: IdentityProviderRow) {
    if (
      !row.isEnabled ||
      (await dialog.confirm({
        title: t("providers.disableTitle", { name: row.displayName }),
        text: t("providers.disableText"),
        confirmLabel: t("providers.disable"),
        tone: "danger",
      }))
    )
      setEnabled.mutate({ row, enabled: !row.isEnabled });
  }

  return (
    <>
      <PageHeader
        title={t("providers.title")}
        subtitle={t("providers.subtitle")}
        actions={
          <Button variant="contained" onClick={() => setEditing("new")}>
            {t("providers.add")}
          </Button>
        }
      />
      <Alert severity="info" variant="outlined" sx={{ mb: 2 }}>
        {t("providers.notice")}
      </Alert>
      <ProblemAlert error={providers.error ?? setEnabled.error ?? undefined} />
      {providers.data && providers.data.length === 0 ? (
        <EmptyState title={t("providers.empty")} />
      ) : providers.data ? (
        <Paper variant="outlined" sx={{ overflowX: "auto" }}>
          <Table size="small" aria-label={t("providers.title")}>
            <TableHead>
              <TableRow>
                <TableCell>{t("providers.name")}</TableCell>
                <TableCell>{t("providers.type")}</TableCell>
                <TableCell>{t("providers.status")}</TableCell>
                <TableCell align="right">{t("providers.accounts")}</TableCell>
                <TableCell />
              </TableRow>
            </TableHead>
            <TableBody>
              {providers.data.map((row) => (
                <TableRow key={row.identityProviderId} hover>
                  <TableCell>
                    <Button
                      variant="text"
                      aria-label={t("providers.edit", { name: row.displayName })}
                      onClick={() => setEditing(row)}
                      sx={{ p: 0, minWidth: 0, textTransform: "none", textAlign: "left" }}
                    >
                      {row.displayName}
                    </Button>
                    <Typography variant="caption" color="text.secondary" sx={{ display: "block" }}>
                      <Identifier>{row.providerKey}</Identifier>
                    </Typography>
                  </TableCell>
                  <TableCell>{t(`providers.types.${row.providerType}`)}</TableCell>
                  <TableCell>
                    <StatusChip
                      label={t(row.isEnabled ? "providers.enabled" : "providers.disabled")}
                      tone={row.isEnabled ? "success" : "default"}
                    />
                  </TableCell>
                  <TableCell align="right">{toNumber(row.linkedAccounts)}</TableCell>
                  <TableCell align="right">
                    <Button
                      size="small"
                      variant="outlined"
                      color={row.isEnabled ? "error" : "primary"}
                      disabled={setEnabled.isPending}
                      aria-label={t(row.isEnabled ? "providers.disableNamed" : "providers.enableNamed", { name: row.displayName })}
                      onClick={() => void toggle(row)}
                    >
                      {t(row.isEnabled ? "providers.disable" : "providers.enable")}
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Paper>
      ) : null}
      {editing ? <ProviderFormDialog existing={editing === "new" ? undefined : editing} onClose={() => setEditing(null)} /> : null}
    </>
  );
}

/** Adds settings, or edits them. Only the fields of the kind show; an empty secret keeps the stored one. */
function ProviderFormDialog({ existing, onClose }: { existing?: IdentityProviderRow; onClose: () => void }) {
  const { t } = useTranslation("operator");
  const dialog = useDialog();
  const [type, setType] = useState<LoginProvider>(existing?.providerType ?? "google");
  const [form, setForm] = useState({
    providerKey: "",
    displayName: existing?.displayName ?? "",
    clientId: existing?.clientId ?? "",
    clientSecret: "",
    authority: existing?.authority ?? "",
    appleTeamId: existing?.appleTeamId ?? "",
    appleKeyId: existing?.appleKeyId ?? "",
  });
  const set = (name: keyof typeof form) => (value: string) => setForm((f) => ({ ...f, [name]: value }));

  const save = useApiMutation(
    () => {
      const settings = {
        displayName: form.displayName,
        clientId: form.clientId,
        clientSecret: form.clientSecret,
        authority: type === "microsoft" ? form.authority : null,
        appleTeamId: type === "apple" ? form.appleTeamId : null,
        appleKeyId: type === "apple" ? form.appleKeyId : null,
      };
      return unwrap(
        existing
          ? tenancy.PUT("/api/tenancy/v1/operator/identity-providers/{id}", {
              params: { path: { id: toNumber(existing.identityProviderId) }, header: { "If-Match": existing.eTag } },
              body: settings,
            })
          : tenancy.POST("/api/tenancy/v1/operator/identity-providers", {
              body: { ...settings, providerType: type, providerKey: form.providerKey },
            }),
      );
    },
    {
      invalidate: [providersKey],
      success: (saved) => t(existing ? "providers.saved" : "providers.added", { name: saved.displayName }),
      onSuccess: onClose,
    },
  );
  const remove = useApiMutation(
    () =>
      unwrapEmpty(
        tenancy.DELETE("/api/tenancy/v1/operator/identity-providers/{id}", {
          params: { path: { id: toNumber(existing!.identityProviderId) } },
        }),
      ),
    { invalidate: [providersKey], success: t("providers.deleted", { name: existing?.displayName }), onSuccess: onClose },
  );

  async function askDelete(row: IdentityProviderRow) {
    if (
      await dialog.confirm({
        title: t("providers.deleteTitle", { name: row.displayName }),
        text: t("providers.deleteText"),
        confirmLabel: t("providers.delete"),
        tone: "danger",
      })
    )
      remove.mutate();
  }

  async function copyCallback(url: string) {
    try {
      await navigator.clipboard.writeText(url);
      snackbar.info(t("providers.copied"));
    } catch {
      // No clipboard (an insecure page): the address is on screen to select.
    }
  }

  const fieldError = (name: string) => (save.error instanceof ApiError ? save.error.forField(name)?.message : undefined);
  const field = (name: keyof typeof form, label: string, extra: { required?: boolean; helperText?: string } = {}) => ({
    label,
    required: extra.required ?? false,
    value: form[name],
    onChange: (e: { target: { value: string } }) => set(name)(e.target.value),
    error: Boolean(fieldError(name)),
    helperText: fieldError(name) ?? extra.helperText,
  });
  const clientIdLabel = t(
    type === "apple"
      ? "providers.fields.clientIdApple"
      : type === "facebook"
        ? "providers.fields.clientIdFacebook"
        : "providers.fields.clientId",
  );
  const secretLabel = t(type === "apple" ? "providers.fields.clientSecretApple" : "providers.fields.clientSecret");

  return (
    <FormDialog
      open
      title={existing ? t("providers.editTitle", { name: existing.displayName }) : t("providers.addTitle")}
      submitLabel={t(existing ? "providers.saveSubmit" : "providers.addSubmit")}
      busy={save.isPending}
      onSubmit={() => save.mutate()}
      onClose={onClose}
      maxWidth="sm"
    >
      {save.error && !(save.error instanceof ApiError && save.error.fieldErrors.length > 0) ? (
        <Alert severity="error">{save.error.message}</Alert>
      ) : null}
      <TextField
        select
        label={t("providers.fields.providerType")}
        value={type}
        disabled={Boolean(existing)}
        onChange={(e) => setType(e.target.value as LoginProvider)}
      >
        {kinds.map((kind) => (
          <MenuItem key={kind} value={kind}>
            {t(`providers.types.${kind}`)}
          </MenuItem>
        ))}
      </TextField>
      {existing ? (
        <Typography variant="body2" color="text.secondary">
          <Identifier>{existing.providerKey}</Identifier>
        </Typography>
      ) : (
        <TextField
          {...field("providerKey", t("providers.fields.providerKey"), {
            required: true,
            helperText: t("providers.fields.providerKeyHelp"),
          })}
        />
      )}
      <TextField
        {...field("displayName", t("providers.fields.displayName"), { required: true, helperText: t("providers.fields.displayNameHelp") })}
        autoFocus
      />
      <TextField {...field("clientId", clientIdLabel)} />
      <TextField
        {...field("clientSecret", secretLabel, {
          helperText: existing?.hasSecret ? t("providers.fields.secretStored") : t("providers.fields.secretMissing"),
        })}
        type="password"
        autoComplete="new-password"
        multiline={type === "apple"}
        minRows={type === "apple" ? 3 : undefined}
      />
      {type === "microsoft" ? (
        <TextField {...field("authority", t("providers.fields.authority"), { helperText: t("providers.fields.authorityHelp") })} />
      ) : null}
      {type === "apple" ? (
        <Stack direction="row" spacing={2}>
          <TextField {...field("appleTeamId", t("providers.fields.appleTeamId"))} sx={{ flex: 1 }} />
          <TextField {...field("appleKeyId", t("providers.fields.appleKeyId"))} sx={{ flex: 1 }} />
        </Stack>
      ) : null}
      {existing ? (
        <Box>
          <Typography variant="caption" color="text.secondary">
            {t("providers.callback")}
          </Typography>
          <Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
            <Typography variant="body2" sx={{ overflowWrap: "anywhere", flex: 1 }}>
              <Identifier>{existing.callbackUrl}</Identifier>
            </Typography>
            <Button size="small" onClick={() => void copyCallback(existing.callbackUrl)}>
              {t("common:actions.copy")}
            </Button>
          </Stack>
          <Typography variant="caption" color="text.secondary">
            {t("providers.callbackHelp")}
          </Typography>
        </Box>
      ) : null}
      {existing ? (
        <Box>
          <Button
            variant="outlined"
            color="error"
            disabled={remove.isPending || toNumber(existing.linkedAccounts) > 0}
            onClick={() => void askDelete(existing)}
          >
            {t("providers.deleteNamed", { name: existing.displayName })}
          </Button>
          {toNumber(existing.linkedAccounts) > 0 ? (
            <Typography variant="caption" color="text.secondary" sx={{ display: "block", mt: 0.5 }}>
              {t("providers.hasAccounts")}
            </Typography>
          ) : null}
        </Box>
      ) : null}
    </FormDialog>
  );
}
