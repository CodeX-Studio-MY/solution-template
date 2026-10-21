import { useMemo, useState } from "react";
import { useNavigate } from "react-router";
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
import { useTranslation } from "react-i18next";
import { ApiError, plans, unwrap, useApiMutation, useApiQuery, useDialog } from "@saaskit/web-core";
import { EmptyState, FormDialog, PageHeader, ProblemAlert, StatusChip } from "@saaskit/ui-kit";
import { tenancy, tenantsKey } from "../api";
import { statusTone } from "./statusTone";

/** Every tenant, and onboarding a new one. */
export function Component() {
  const { t } = useTranslation("operator");
  const navigate = useNavigate();
  const [search, setSearch] = useState("");
  const [onboarding, setOnboarding] = useState(false);
  const tenants = useApiQuery(tenantsKey, (signal) => unwrap(tenancy.GET("/api/tenancy/v1/operator/tenants", { signal })));
  const shown = useMemo(() => {
    const term = search.trim().toLowerCase();
    return (tenants.data ?? []).filter((x) => !term || x.slug.includes(term) || x.displayName.toLowerCase().includes(term));
  }, [tenants.data, search]);

  return (
    <>
      <PageHeader
        title={t("tenants.title")}
        subtitle={t("tenants.subtitle")}
        actions={
          <Button variant="contained" onClick={() => setOnboarding(true)}>
            {t("tenants.onboard")}
          </Button>
        }
      />
      <ProblemAlert error={tenants.error ?? undefined} />
      {tenants.data && tenants.data.length === 0 ? (
        <EmptyState title={t("tenants.empty")} />
      ) : (
        <Stack spacing={2}>
          <TextField
            size="small"
            type="search"
            label={t("tenants.search")}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            sx={{ maxWidth: 320 }}
          />
          <Paper variant="outlined" sx={{ overflowX: "auto" }}>
            <Table size="small" aria-label={t("tenants.title")}>
              <TableHead>
                <TableRow>
                  <TableCell>{t("tenants.name")}</TableCell>
                  <TableCell>{t("tenants.slug")}</TableCell>
                  <TableCell>{t("tenants.plan")}</TableCell>
                  <TableCell>{t("tenants.status")}</TableCell>
                  <TableCell align="right">{t("tenants.modules")}</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {shown.map((tenant) => (
                  <TableRow key={tenant.id} hover onClick={() => void navigate(`/tenants/${tenant.id}`)} sx={{ cursor: "pointer" }}>
                    <TableCell>
                      <Button
                        variant="text"
                        onClick={() => void navigate(`/tenants/${tenant.id}`)}
                        sx={{ p: 0, minWidth: 0, textTransform: "none" }}
                      >
                        {tenant.displayName}
                      </Button>
                    </TableCell>
                    <TableCell sx={{ fontFamily: "monospace" }}>{tenant.slug}</TableCell>
                    <TableCell>{tenant.plan}</TableCell>
                    <TableCell>
                      <StatusChip label={t(`statuses.${tenant.status}`)} tone={statusTone[tenant.status]} />
                    </TableCell>
                    <TableCell align="right">{tenant.modules.length}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </Paper>
        </Stack>
      )}
      {onboarding ? <OnboardDialog onClose={() => setOnboarding(false)} /> : null}
    </>
  );
}

function OnboardDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation("operator");
  const dialog = useDialog();
  const [form, setForm] = useState({ slug: "", displayName: "", plan: plans[0] as string, adminEmail: "", adminName: "" });
  const set = (name: keyof typeof form) => (value: string) => setForm((f) => ({ ...f, [name]: value }));
  const onboard = useApiMutation(() => unwrap(tenancy.POST("/api/tenancy/v1/operator/tenants", { body: form })), {
    invalidate: [tenantsKey],
    onSuccess: async (result) => {
      onClose();
      await dialog.alert({
        title: t("onboard.doneTitle", { name: form.displayName }),
        text: result.adminInvitationUrl ? t("onboard.doneText", { url: result.adminInvitationUrl }) : t("onboard.doneResumed"),
        confirmLabel: t("common:actions.close"),
      });
    },
  });
  const fieldError = (name: string) => (onboard.error instanceof ApiError ? onboard.error.forField(name)?.message : undefined);
  const field = (name: keyof typeof form, extra: { type?: string; helperText?: string } = {}) => ({
    label: t(`onboard.${name}`),
    required: true,
    value: form[name],
    onChange: (e: { target: { value: string } }) => set(name)(e.target.value),
    error: Boolean(fieldError(name)),
    helperText: fieldError(name) ?? extra.helperText,
    type: extra.type,
  });

  return (
    <FormDialog
      open
      title={t("onboard.title")}
      text={t("onboard.text")}
      submitLabel={t("onboard.submit")}
      busy={onboard.isPending}
      onSubmit={() => onboard.mutate()}
      onClose={onClose}
      maxWidth="sm"
    >
      <TextField {...field("displayName")} autoFocus />
      <TextField {...field("slug", { helperText: t("onboard.slugHelp") })} />
      <TextField {...field("plan")} select>
        {plans.map((plan) => (
          <MenuItem key={plan} value={plan}>
            {plan}
          </MenuItem>
        ))}
      </TextField>
      <TextField {...field("adminEmail", { type: "email" })} />
      <TextField {...field("adminName")} />
    </FormDialog>
  );
}
