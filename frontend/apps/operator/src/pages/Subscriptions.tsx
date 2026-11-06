import { useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Checkbox from "@mui/material/Checkbox";
import Paper from "@mui/material/Paper";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Tooltip from "@mui/material/Tooltip";
import Typography from "@mui/material/Typography";
import { useTranslation } from "react-i18next";
import { ApiError, modules as platformModules, platformDefaults, toNumber, unwrap, useApiMutation, useDialog, useFormatters } from "@saaskit/web-core";
import { EmptyState, FormDialog, PageHeader, ProblemAlert, StatusChip, tokens } from "@saaskit/ui-kit";
import { plansKey, tenancy, tenantsKey, useModules, usePlans, type ModuleRow, type PlanRow } from "../api";

/** The platform's own modules are in every plan. Once a plan has one, the server refuses to take it out, so the matrix doesn't offer to. */
const lockedModules = new Set<string>(platformModules.filter((m) => m.inEveryPlan).map((m) => m.key));

/** The plans and what each includes: plans across the top, modules down the side, a checkbox where they meet. */
export function Component() {
  const { t } = useTranslation("operator");
  const [editing, setEditing] = useState<PlanRow | "new" | null>(null);
  const plans = usePlans();
  const modules = useModules();

  return (
    <>
      <PageHeader
        title={t("subscriptions.title")}
        subtitle={t("subscriptions.subtitle")}
        actions={
          <Button variant="contained" onClick={() => setEditing("new")}>
            {t("subscriptions.add")}
          </Button>
        }
      />
      <ProblemAlert error={plans.error ?? modules.error ?? undefined} />
      {plans.data && modules.data ? (
        plans.data.length === 0 ? (
          <EmptyState title={t("subscriptions.empty")} hint={t("subscriptions.emptyHint")} />
        ) : (
          <>
            <Matrix plans={plans.data} modules={modules.data} onOpen={setEditing} />
            <Typography variant="body2" color="text.secondary" sx={{ mt: 1.5 }}>
              {t("subscriptions.moduleNote")}
            </Typography>
          </>
        )
      ) : null}
      {editing ? <PlanFormDialog existing={editing === "new" ? undefined : editing} onClose={() => setEditing(null)} /> : null}
    </>
  );
}

const stickyFirst = {
  position: "sticky",
  left: 0,
  zIndex: 2,
  bgcolor: "background.paper",
  borderRight: 1,
  borderColor: "divider",
} as const;

/**
 * A real table: the plan is the column header, the module the row header, and each checkbox is named by both ("Billing in Lite").
 * The first column stays in place while the rest scrolls sideways, which is how it fits a phone.
 */
function Matrix({ plans, modules, onOpen }: { plans: readonly PlanRow[]; modules: readonly ModuleRow[]; onOpen: (plan: PlanRow) => void }) {
  const { t } = useTranslation("operator");
  const format = useFormatters();
  const dialog = useDialog();

  const setModule = useApiMutation(
    ({ plan, module, add }: { plan: PlanRow; module: ModuleRow; add: boolean }) => {
      const params = { params: { path: { id: toNumber(plan.planId), moduleKey: module.moduleKey } } };
      return unwrap(
        add
          ? tenancy.PUT("/api/tenancy/v1/operator/plans/{id}/modules/{moduleKey}", params)
          : tenancy.DELETE("/api/tenancy/v1/operator/plans/{id}/modules/{moduleKey}", params),
      );
    },
    {
      // Every tenant on the plan changed too.
      invalidate: [plansKey, tenantsKey],
      success: (_, { plan, module, add }) =>
        t(add ? "subscriptions.moduleAdded" : "subscriptions.moduleRemoved", { module: module.displayName, plan: plan.displayName }),
    },
  );

  /** A change here reaches every tenant on the plan at once, so it is confirmed with their number. */
  async function toggle(plan: PlanRow, module: ModuleRow, add: boolean) {
    const count = toNumber(plan.tenantCount);
    const names = { module: module.displayName, plan: plan.displayName, count };
    const confirmed = await dialog.confirm({
      title: t(add ? "subscriptions.addModuleTitle" : "subscriptions.removeModuleTitle", names),
      text: t(add ? "subscriptions.addModuleText" : "subscriptions.removeModuleText", names),
      confirmLabel: t(add ? "subscriptions.addModule" : "subscriptions.removeModule"),
      tone: add ? "default" : "danger",
    });
    if (confirmed) setModule.mutate({ plan, module, add });
  }

  return (
    <Paper variant="outlined" sx={{ overflowX: "auto" }}>
      <Table
        aria-label={t("subscriptions.matrix")}
        sx={{ borderCollapse: "separate", borderSpacing: 0, "& .MuiTableCell-root": { verticalAlign: "top" } }}
      >
        <TableHead>
          <TableRow>
            <TableCell component="th" scope="col" sx={{ ...stickyFirst, minWidth: 190 }}>
              {t("subscriptions.module")}
            </TableCell>
            {plans.map((plan) => (
              <TableCell key={plan.planId} component="th" scope="col" sx={{ minWidth: 150 }}>
                <Button
                  variant="text"
                  aria-label={t("subscriptions.openPlan", { name: plan.displayName })}
                  onClick={() => onOpen(plan)}
                  sx={{ p: 0, minWidth: 0, textTransform: "none", fontWeight: 600, textAlign: "left" }}
                >
                  {plan.displayName}
                </Button>
                {plan.isActive ? null : (
                  <Box component="span" sx={{ ml: 1 }}>
                    <StatusChip label={t("subscriptions.retired")} />
                  </Box>
                )}
                <Typography variant="body2" sx={{ fontWeight: 400 }}>
                  {plan.monthlyPrice === null
                    ? t("subscriptions.noPrice")
                    : t("subscriptions.perMonth", { price: format.money(plan.monthlyPrice, plan.currency) })}
                </Typography>
                <Typography variant="body2" color="text.secondary" sx={{ fontWeight: 400 }}>
                  {t("subscriptions.tenantCount", { count: toNumber(plan.tenantCount) })}
                </Typography>
              </TableCell>
            ))}
          </TableRow>
        </TableHead>
        <TableBody>
          {modules.map((module) => (
            <TableRow key={module.moduleKey} hover>
              <TableCell component="th" scope="row" sx={{ ...stickyFirst, fontWeight: 500 }}>
                {module.displayName}
                <Typography variant="caption" color="text.secondary" sx={{ display: "block", fontFamily: tokens.operator.fontMono }}>
                  {module.moduleKey}
                </Typography>
              </TableCell>
              {plans.map((plan) => {
                const included = plan.modules.includes(module.moduleKey);
                const locked = included && lockedModules.has(module.moduleKey);
                const checkbox = (
                  <Checkbox
                    checked={included}
                    disabled={locked || setModule.isPending}
                    onChange={() => void toggle(plan, module, !included)}
                    slotProps={{
                      input: { "aria-label": t("subscriptions.includes", { module: module.displayName, plan: plan.displayName }) },
                    }}
                  />
                );
                return (
                  <TableCell key={plan.planId} sx={{ py: 0.5 }}>
                    {locked ? (
                      <Tooltip title={t("subscriptions.inEveryPlan")} describeChild>
                        <span>{checkbox}</span>
                      </Tooltip>
                    ) : (
                      checkbox
                    )}
                  </TableCell>
                );
              })}
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </Paper>
  );
}

/** Adds a plan, or edits one (and retires or restores it). The key is chosen once and never changes. */
function PlanFormDialog({ existing, onClose }: { existing?: PlanRow; onClose: () => void }) {
  const { t } = useTranslation("operator");
  const dialog = useDialog();
  const [form, setForm] = useState({
    planKey: "",
    displayName: existing?.displayName ?? "",
    description: existing?.description ?? "",
    monthlyPrice: existing?.monthlyPrice === null || existing?.monthlyPrice === undefined ? "" : String(existing.monthlyPrice),
    currency: existing?.currency ?? platformDefaults.currency,
    sortOrder: existing ? String(existing.sortOrder) : "",
  });
  const set = (name: keyof typeof form) => (value: string) => setForm((f) => ({ ...f, [name]: value }));

  const save = useApiMutation(
    () => {
      const monthlyPrice = form.monthlyPrice.trim() === "" ? null : Number(form.monthlyPrice);
      const common = { displayName: form.displayName, description: form.description.trim() || null, monthlyPrice };
      return unwrap(
        existing
          ? tenancy.PUT("/api/tenancy/v1/operator/plans/{id}", {
              params: { path: { id: toNumber(existing.planId) }, header: { "If-Match": existing.eTag } },
              body: { ...common, currency: form.currency, sortOrder: Number(form.sortOrder) },
            })
          : tenancy.POST("/api/tenancy/v1/operator/plans", {
              body: {
                ...common,
                planKey: form.planKey,
                currency: form.currency || null,
                ...(form.sortOrder.trim() ? { sortOrder: Number(form.sortOrder) } : {}),
              },
            }),
      );
    },
    {
      invalidate: [plansKey],
      success: (saved) => t(existing ? "subscriptions.saved" : "subscriptions.added", { name: saved.displayName }),
      onSuccess: onClose,
    },
  );
  const setActive = useApiMutation(
    (active: boolean) =>
      unwrap(
        tenancy.PUT("/api/tenancy/v1/operator/plans/{id}/active", {
          params: { path: { id: toNumber(existing!.planId) }, header: { "If-Match": existing!.eTag } },
          body: { active },
        }),
      ),
    {
      invalidate: [plansKey],
      success: (saved) => t(saved.isActive ? "subscriptions.restoredDone" : "subscriptions.retiredDone", { name: saved.displayName }),
      onSuccess: onClose,
    },
  );

  async function toggleActive(plan: PlanRow) {
    if (
      !plan.isActive ||
      (await dialog.confirm({
        title: t("subscriptions.retireTitle", { name: plan.displayName }),
        text: t("subscriptions.retireText", { count: toNumber(plan.tenantCount) }),
        confirmLabel: t("subscriptions.retire"),
        tone: "danger",
      }))
    )
      setActive.mutate(!plan.isActive);
  }

  const fieldError = (name: string) => (save.error instanceof ApiError ? save.error.forField(name)?.message : undefined);
  const field = (name: keyof typeof form, extra: { required?: boolean; helperText?: string } = {}) => ({
    label: t(`subscriptions.fields.${name}`),
    required: extra.required ?? false,
    value: form[name],
    onChange: (e: { target: { value: string } }) => set(name)(e.target.value),
    error: Boolean(fieldError(name)),
    helperText: fieldError(name) ?? extra.helperText,
  });

  return (
    <FormDialog
      open
      title={existing ? t("subscriptions.editTitle", { name: existing.displayName }) : t("subscriptions.addTitle")}
      submitLabel={t(existing ? "subscriptions.saveSubmit" : "subscriptions.addSubmit")}
      busy={save.isPending}
      onSubmit={() => save.mutate()}
      onClose={onClose}
      maxWidth="sm"
    >
      {save.error && !(save.error instanceof ApiError && save.error.fieldErrors.length > 0) ? (
        <Alert severity="error">{save.error.message}</Alert>
      ) : null}
      {existing ? (
        <Typography variant="body2" color="text.secondary" sx={{ fontFamily: tokens.operator.fontMono }}>
          {existing.planKey}
        </Typography>
      ) : (
        <TextField {...field("planKey", { required: true, helperText: t("subscriptions.fields.planKeyHelp") })} autoFocus />
      )}
      <TextField {...field("displayName", { required: true })} autoFocus={Boolean(existing)} />
      <TextField {...field("description")} multiline minRows={2} />
      <Stack direction="row" spacing={2}>
        <TextField
          {...field("monthlyPrice", { helperText: t("subscriptions.fields.monthlyPriceHelp") })}
          type="number"
          slotProps={{ htmlInput: { min: 0, step: "0.01" } }}
          sx={{ flex: 2 }}
        />
        <TextField
          {...field("currency", { required: true })}
          slotProps={{ htmlInput: { maxLength: 3 } }}
          onChange={(e) => set("currency")(e.target.value.toUpperCase())}
          sx={{ flex: 1 }}
        />
      </Stack>
      <TextField
        {...field("sortOrder", { required: Boolean(existing), helperText: t("subscriptions.fields.sortOrderHelp") })}
        type="number"
        slotProps={{ htmlInput: { min: 1, max: 255, step: 1 } }}
        sx={{ maxWidth: 160 }}
      />
      {existing ? (
        <Box>
          <Button
            variant="outlined"
            color={existing.isActive ? "error" : "primary"}
            disabled={setActive.isPending}
            onClick={() => void toggleActive(existing)}
          >
            {t(existing.isActive ? "subscriptions.retire" : "subscriptions.restore")}
          </Button>
        </Box>
      ) : null}
    </FormDialog>
  );
}
