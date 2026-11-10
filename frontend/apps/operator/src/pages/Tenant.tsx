import { useMemo, useState, type ReactNode } from "react";
import { Link as RouterLink, useParams } from "react-router";
import Button from "@mui/material/Button";
import Checkbox from "@mui/material/Checkbox";
import FormControlLabel from "@mui/material/FormControlLabel";
import Link from "@mui/material/Link";
import MenuItem from "@mui/material/MenuItem";
import Paper from "@mui/material/Paper";
import Stack from "@mui/material/Stack";
import Switch from "@mui/material/Switch";
import Tab from "@mui/material/Tab";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import Tabs from "@mui/material/Tabs";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { useTranslation } from "react-i18next";
import { ApiError, modules, unwrap, useApiMutation, useApiQuery, useDialog, useFormatters, type Role } from "@saaskit/web-core";
import {
  ActivityTable,
  EmptyState,
  PageHeader,
  ProblemAlert,
  RoleSelect,
  SettingsForm,
  StatusChip,
  TenantProfileForm,
  type SettingsView,
  type Tone,
  type TenantProfile,
} from "@saaskit/ui-kit";
import { feedbackKey, plansKey, tenancy, tenantsKey, useModules, usePlans, type SupportAccess, type TenantSummary } from "../api";
import { FeedbackTable } from "../components/FeedbackTable";
import { statusTone } from "./statusTone";
import { TenantAccess, TenantLogins } from "./TenantAccess";

type TabName = "overview" | "settings" | "features" | "activity" | "access" | "logins";

/** One tenant: profile, plan and add-ons, support policy, lifecycle, and (as an operator) every setting and flag. */
export function Component() {
  const { t } = useTranslation("operator");
  const { id = "" } = useParams();
  const [tab, setTab] = useState<TabName>("overview");
  const tenant = useApiQuery([...tenantsKey, id], (signal) =>
    unwrap(tenancy.GET("/api/tenancy/v1/operator/tenants/{id}", { params: { path: { id } }, signal })),
  );
  const data = tenant.data;

  return (
    <>
      <Link component={RouterLink} to="/" underline="hover" sx={{ display: "inline-block", mb: 2 }}>
        {t("tenant.back")}
      </Link>
      <ProblemAlert error={tenant.error ?? undefined} />
      {data ? (
        <>
          <PageHeader
            title={data.displayName}
            subtitle={data.slug}
            actions={<StatusChip label={t(`statuses.${data.status}`)} tone={statusTone[data.status]} />}
          />
          <Tabs value={tab} onChange={(_, next: TabName) => setTab(next)} variant="scrollable" allowScrollButtonsMobile sx={{ mb: 3 }}>
            {(["overview", "settings", "features", "activity", "access", "logins"] as const).map((name) => (
              <Tab key={name} value={name} label={t(`tenant.tabs.${name}`)} />
            ))}
          </Tabs>
          {tab === "overview" ? <Overview tenant={data} /> : null}
          {tab === "settings" ? <Settings tenantId={data.id} /> : null}
          {tab === "features" ? <Features tenantId={data.id} /> : null}
          {tab === "activity" ? <Activity tenantId={data.id} /> : null}
          {tab === "access" ? <TenantAccess tenantId={data.id} /> : null}
          {tab === "logins" ? <TenantLogins tenantId={data.id} /> : null}
        </>
      ) : null}
    </>
  );
}

function Section({ title, hint, children }: { title: string; hint?: string; children: ReactNode }) {
  return (
    <Paper variant="outlined" component="section" aria-label={title} sx={{ p: 2 }}>
      <Typography variant="h3" component="h2" sx={{ mb: hint ? 0.5 : 2 }}>
        {title}
      </Typography>
      {hint ? (
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          {hint}
        </Typography>
      ) : null}
      {children}
    </Paper>
  );
}

function Overview({ tenant }: { tenant: TenantSummary }) {
  const { t } = useTranslation("operator");
  const dialog = useDialog();
  const id = tenant.id;
  const path = { params: { path: { id } } };
  const refresh = { invalidate: [tenantsKey] };
  // The plans and modules come from the database, so a plan added in Subscriptions is here at once.
  const plans = usePlans();
  const moduleList = useModules();
  const currentPlan = plans.data?.find((plan) => plan.planKey === tenant.plan);

  const profile = useMemo<TenantProfile>(
    () => ({ displayName: tenant.displayName, timeZone: tenant.timeZone, currency: tenant.currency, locale: tenant.locale }),
    [tenant],
  );
  const saveProfile = useApiMutation(
    (body: TenantProfile) => unwrap(tenancy.PUT("/api/tenancy/v1/operator/tenants/{id}/profile", { ...path, body })),
    {
      ...refresh,
      success: t("tenant.profileSaved"),
    },
  );
  const changePlan = useApiMutation(
    (plan: string) => unwrap(tenancy.PUT("/api/tenancy/v1/operator/tenants/{id}/plan", { ...path, body: { plan } })),
    {
      invalidate: [tenantsKey, plansKey],
      success: t("tenant.planSaved"),
    },
  );
  const setModule = useApiMutation(
    ({ module, on }: { module: string; on: boolean }) =>
      unwrap(
        on
          ? tenancy.PUT("/api/tenancy/v1/operator/tenants/{id}/modules/{module}", { params: { path: { id, module } } })
          : tenancy.DELETE("/api/tenancy/v1/operator/tenants/{id}/modules/{module}", { params: { path: { id, module } } }),
      ),
    { ...refresh, success: t("tenant.moduleSaved") },
  );
  const setApproval = useApiMutation(
    (required: boolean) => unwrap(tenancy.PUT("/api/tenancy/v1/operator/tenants/{id}/support-approval", { ...path, body: { required } })),
    { ...refresh, success: t("tenant.supportApprovalSaved") },
  );

  async function askPlan(plan: string) {
    if (plan === tenant.plan) return;
    const name = plans.data?.find((p) => p.planKey === plan)?.displayName ?? plan;
    if (
      await dialog.confirm({
        title: t("tenant.planTitle", { name: tenant.displayName, plan: name }),
        text: t("tenant.planText"),
        tone: "danger",
      })
    )
      changePlan.mutate(plan);
  }

  return (
    <Stack spacing={3}>
      <Section title={t("tenant.profile")}>
        <TenantProfileForm
          value={profile}
          onSave={(body) => saveProfile.mutate(body)}
          saving={saveProfile.isPending}
          error={saveProfile.error}
        />
      </Section>
      <Section title={t("tenant.plan")}>
        <TextField
          select
          size="small"
          label={t("tenant.plan")}
          value={plans.data ? tenant.plan : ""}
          onChange={(e) => void askPlan(e.target.value)}
          sx={{ minWidth: 200 }}
        >
          {/* A retired plan is offered only to the tenant already on it. */}
          {(plans.data ?? [])
            .filter((plan) => plan.isActive || plan.planKey === tenant.plan)
            .map((plan) => (
              <MenuItem key={plan.planKey} value={plan.planKey}>
                {plan.displayName}
              </MenuItem>
            ))}
        </TextField>
      </Section>
      <Section title={t("tenant.modules")} hint={t("tenant.modulesHint")}>
        <Stack>
          {(moduleList.data ?? []).map((module) => {
            const inPlan = currentPlan?.modules.includes(module.moduleKey) ?? false;
            return (
              <FormControlLabel
                key={module.moduleKey}
                control={
                  <Checkbox
                    checked={inPlan || tenant.modules.includes(module.moduleKey)}
                    disabled={inPlan || setModule.isPending}
                    onChange={(e) => setModule.mutate({ module: module.moduleKey, on: e.target.checked })}
                  />
                }
                label={inPlan ? `${module.displayName} (${t("tenant.inPlan")})` : module.displayName}
              />
            );
          })}
        </Stack>
      </Section>
      <Section title={t("tenant.support")} hint={t("tenant.supportHint")}>
        <SupportRequest tenant={tenant} />
        <SupportRequests tenantId={tenant.id} />
      </Section>
      <Section title={t("feedback.recent")} hint={t("feedback.recentHint")}>
        <RecentFeedback tenantId={tenant.id} />
      </Section>
      <Section title={t("tenants.status")}>
        <Stack spacing={2}>
          <FormControlLabel
            control={
              <Switch
                checked={tenant.requireSupportApproval}
                onChange={(e) => setApproval.mutate(e.target.checked)}
                disabled={setApproval.isPending}
              />
            }
            label={t("tenant.supportApproval")}
          />
          <Lifecycle tenant={tenant} />
        </Stack>
      </Section>
    </Stack>
  );
}

/** The tenant's latest feedback, with the same status menu as the Feedback page. */
function RecentFeedback({ tenantId }: { tenantId: string }) {
  const { t } = useTranslation("operator");
  const list = useApiQuery([...feedbackKey, { tenantId, recent: true }], (signal) =>
    unwrap(tenancy.GET("/api/tenancy/v1/operator/feedback", { params: { query: { tenantId, Page: 1, PageSize: 5 } }, signal })),
  );
  const items = list.data?.items ?? [];
  return (
    <>
      <ProblemAlert error={list.error ?? undefined} />
      {list.data && items.length === 0 ? (
        <EmptyState title={t("feedback.empty")} />
      ) : (
        <Paper variant="outlined" sx={{ overflowX: "auto" }}>
          <FeedbackTable rows={items} showTenant={false} />
        </Paper>
      )}
      <Link component={RouterLink} to="/feedback" underline="hover" sx={{ display: "inline-block", mt: 2 }}>
        {t("feedback.viewAll")}
      </Link>
    </>
  );
}

/** The moves the tenant's status allows (the server enforces the same rules). */
function Lifecycle({ tenant }: { tenant: TenantSummary }) {
  const { t } = useTranslation("operator");
  const dialog = useDialog();
  const path = { params: { path: { id: tenant.id } } };
  const calls = {
    suspend: () => tenancy.POST("/api/tenancy/v1/operator/tenants/{id}/suspend", path),
    resume: () => tenancy.POST("/api/tenancy/v1/operator/tenants/{id}/resume", path),
    offboard: () => tenancy.POST("/api/tenancy/v1/operator/tenants/{id}/offboard", path),
    cancel: () => tenancy.POST("/api/tenancy/v1/operator/tenants/{id}/cancel-offboarding", path),
    complete: () => tenancy.POST("/api/tenancy/v1/operator/tenants/{id}/complete-deletion", path),
  };
  type Move = keyof typeof calls;
  const move = useApiMutation((name: Move) => unwrap(calls[name]()), {
    invalidate: [tenantsKey],
    success: (moved) => t("lifecycle.done", { name: moved.displayName, status: t(`statuses.${moved.status}`) }),
  });
  const available: Record<TenantSummary["status"], Move[]> = {
    provisioning: [],
    active: ["suspend", "offboard"],
    suspended: ["resume", "offboard"],
    pendingDeletion: ["cancel", "complete"],
    deleted: [],
  };
  const risky: Partial<Record<Move, { title: string; text: string }>> = {
    suspend: { title: "lifecycle.suspendTitle", text: "lifecycle.suspendText" },
    offboard: { title: "lifecycle.offboardTitle", text: "lifecycle.offboardText" },
    complete: { title: "lifecycle.completeTitle", text: "lifecycle.completeText" },
  };

  async function run(name: Move) {
    const ask = risky[name];
    if (
      ask &&
      !(await dialog.confirm({
        title: t(ask.title, { name: tenant.displayName }),
        text: t(ask.text),
        confirmLabel: t(`lifecycle.${name}`),
        tone: "danger",
      }))
    )
      return;
    move.mutate(name);
  }

  if (available[tenant.status].length === 0) return null;
  return (
    <Stack direction="row" spacing={1} aria-label={t("tenant.lifecycle")}>
      {available[tenant.status].map((name) => (
        <Button
          key={name}
          variant="outlined"
          color={risky[name] ? "error" : "primary"}
          onClick={() => void run(name)}
          disabled={move.isPending}
        >
          {t(`lifecycle.${name}`)}
        </Button>
      ))}
    </Stack>
  );
}

function Settings({ tenantId }: { tenantId: string }) {
  const all = useApiQuery([...tenantsKey, tenantId, "settings"], (signal) =>
    unwrap(tenancy.GET("/api/tenancy/v1/operator/tenants/{id}/settings", { params: { path: { id: tenantId } }, signal })),
  );
  return (
    <Stack spacing={3}>
      <ProblemAlert error={all.error ?? undefined} />
      {(all.data ?? [])
        .filter((view) => view.settings.length > 0)
        .map((view) => (
          <ModuleSettings key={view.moduleKey} tenantId={tenantId} view={view} />
        ))}
    </Stack>
  );
}

function ModuleSettings({ tenantId, view }: { tenantId: string; view: SettingsView }) {
  const { t } = useTranslation("operator");
  const save = useApiMutation(
    (changes: Record<string, unknown>) =>
      unwrap(
        tenancy.PUT("/api/tenancy/v1/operator/tenants/{id}/settings/{moduleKey}", {
          params: { path: { id: tenantId, moduleKey: view.moduleKey } },
          body: changes as never,
        }),
      ),
    { invalidate: [[...tenantsKey, tenantId, "settings"]], success: t("tenant.settingsSaved") },
  );
  return (
    <Section title={modules.find((m) => m.key === view.moduleKey)?.name ?? view.moduleKey}>
      <SettingsForm view={view} editAll onSave={(changes) => save.mutateAsync(changes)} saving={save.isPending} error={save.error} />
    </Section>
  );
}

function Features({ tenantId }: { tenantId: string }) {
  const { t } = useTranslation("operator");
  const key = [...tenantsKey, tenantId, "features"];
  const features = useApiQuery(key, (signal) =>
    unwrap(tenancy.GET("/api/tenancy/v1/operator/tenants/{id}/features", { params: { path: { id: tenantId } }, signal })),
  );
  const set = useApiMutation(
    ({ feature, enabled }: { feature: string; enabled: boolean | null }) =>
      unwrap(
        tenancy.PUT("/api/tenancy/v1/operator/tenants/{id}/features/{key}", {
          params: { path: { id: tenantId, key: feature } },
          body: { enabled },
        }),
      ),
    { invalidate: [key], success: t("tenant.featureSaved") },
  );
  const onOff = (value: boolean) => (value ? t("common:settingsForm.on") : t("common:settingsForm.off"));
  return (
    <>
      <ProblemAlert error={features.error ?? undefined} />
      <Paper variant="outlined" sx={{ overflowX: "auto" }}>
        <Table size="small" aria-label={t("tenant.tabs.features")}>
          <TableHead>
            <TableRow>
              <TableCell>{t("tenant.tabs.features")}</TableCell>
              <TableCell>{t("tenants.modules")}</TableCell>
              <TableCell />
            </TableRow>
          </TableHead>
          <TableBody>
            {(features.data ?? []).map((feature) => (
              <TableRow key={feature.key}>
                <TableCell>
                  <Typography variant="body2">{feature.description || feature.key}</Typography>
                  <Typography variant="caption" color="text.secondary">
                    {feature.key}
                  </Typography>
                </TableCell>
                <TableCell>{modules.find((m) => m.key === feature.moduleKey)?.name ?? feature.moduleKey}</TableCell>
                <TableCell align="right">
                  <TextField
                    select
                    size="small"
                    aria-label={feature.key}
                    value={feature.override === null ? "default" : String(feature.override)}
                    onChange={(e) =>
                      set.mutate({ feature: feature.key, enabled: e.target.value === "default" ? null : e.target.value === "true" })
                    }
                    disabled={set.isPending}
                    sx={{ minWidth: 160 }}
                  >
                    <MenuItem value="default">{t("tenant.featureDefault", { state: onOff(feature.default) })}</MenuItem>
                    <MenuItem value="true">{onOff(true)}</MenuItem>
                    <MenuItem value="false">{onOff(false)}</MenuItem>
                  </TextField>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Paper>
    </>
  );
}

function SupportRequest({ tenant }: { tenant: TenantSummary }) {
  const { t } = useTranslation("operator");
  const [form, setForm] = useState<{ module: string; role: Role | null; minutes: number; reason: string }>({
    module: tenant.modules[0] ?? "",
    role: "user",
    minutes: 60,
    reason: "",
  });
  const request = useApiMutation(
    () =>
      unwrap(
        tenancy.POST("/api/tenancy/v1/operator/support-access", {
          body: { tenantSlug: tenant.slug, module: form.module, role: form.role ?? "user", minutes: form.minutes, reason: form.reason },
        }),
      ),
    {
      invalidate: [tenantsKey],
      success: (grant) => t("tenant.requested", { status: t(`tenant.supportStatuses.${grant.status}`) }),
      onSuccess: () => setForm((f) => ({ ...f, reason: "" })),
    },
  );
  const fieldError = (name: string) => (request.error instanceof ApiError ? request.error.forField(name)?.message : undefined);
  return (
    <Stack spacing={2} sx={{ maxWidth: 560 }}>
      <Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
        <TextField
          select
          size="small"
          label={t("tenant.module")}
          value={form.module}
          onChange={(e) => setForm((f) => ({ ...f, module: e.target.value }))}
        >
          {tenant.modules.map((key) => (
            <MenuItem key={key} value={key}>
              {modules.find((m) => m.key === key)?.name ?? key}
            </MenuItem>
          ))}
        </TextField>
        <RoleSelect label={t("tenant.role")} value={form.role} allowNone={false} onChange={(role) => setForm((f) => ({ ...f, role }))} />
        <TextField
          size="small"
          type="number"
          label={t("tenant.minutes")}
          value={form.minutes}
          onChange={(e) => setForm((f) => ({ ...f, minutes: Number(e.target.value) }))}
          slotProps={{ htmlInput: { min: 5, max: 480 } }}
          error={Boolean(fieldError("minutes"))}
          helperText={fieldError("minutes")}
        />
      </Stack>
      <TextField
        label={t("tenant.reason")}
        required
        multiline
        minRows={2}
        value={form.reason}
        onChange={(e) => setForm((f) => ({ ...f, reason: e.target.value }))}
        error={Boolean(fieldError("reason"))}
        helperText={fieldError("reason")}
      />
      <div>
        <Button variant="contained" onClick={() => request.mutate()} disabled={request.isPending || !form.reason.trim() || !form.module}>
          {t("tenant.request")}
        </Button>
      </div>
    </Stack>
  );
}

const supportTones: Record<SupportAccess["status"], Tone> = {
  pending: "warning",
  approved: "success",
  denied: "default",
  revoked: "default",
};

/** What was asked of this tenant, and what its admin answered. A request refreshes it, so the new pending row appears at once. */
function SupportRequests({ tenantId }: { tenantId: string }) {
  const { t } = useTranslation("operator");
  const format = useFormatters();
  const list = useApiQuery([...tenantsKey, tenantId, "support-access"], (signal) =>
    unwrap(tenancy.GET("/api/tenancy/v1/operator/tenants/{id}/support-access", { params: { path: { id: tenantId } }, signal })),
  );
  const rows = list.data ?? [];
  return (
    <Stack spacing={1} sx={{ mt: 3 }}>
      <Typography variant="h4" component="h3">
        {t("tenant.supportList")}
      </Typography>
      <ProblemAlert error={list.error ?? undefined} />
      {list.data && rows.length === 0 ? (
        <EmptyState title={t("tenant.supportEmpty")} />
      ) : rows.length > 0 ? (
        <Paper variant="outlined" sx={{ overflowX: "auto" }}>
          <Table size="small" aria-label={t("tenant.supportList")}>
            <TableHead>
              <TableRow>
                <TableCell>{t("tenant.module")}</TableCell>
                <TableCell>{t("tenant.role")}</TableCell>
                <TableCell>{t("tenant.reason")}</TableCell>
                <TableCell>{t("tenant.supportStatus")}</TableCell>
                <TableCell>{t("tenant.supportUntil")}</TableCell>
                <TableCell>{t("tenant.supportDecidedBy")}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {rows.map((grant) => (
                <TableRow key={grant.id}>
                  <TableCell>{modules.find((m) => m.key === grant.module)?.name ?? grant.module}</TableCell>
                  <TableCell>{t(`common:roles.${grant.role}`)}</TableCell>
                  <TableCell>{grant.reason}</TableCell>
                  <TableCell>
                    <StatusChip label={t(`tenant.supportStatuses.${grant.status}`)} tone={supportTones[grant.status]} />
                  </TableCell>
                  <TableCell>{format.dateTime(grant.expiresOn)}</TableCell>
                  <TableCell>
                    {grant.decidedByName ?? (grant.status === "approved" ? t("tenant.supportNoApproval") : t("tenant.supportNobody"))}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Paper>
      ) : null}
    </Stack>
  );
}

function Activity({ tenantId }: { tenantId: string }) {
  const { t } = useTranslation("operator");
  const [paging, setPaging] = useState({ page: 1, pageSize: 25 });
  const log = useApiQuery([...tenantsKey, tenantId, "activity", paging], (signal) =>
    unwrap(
      tenancy.GET("/api/tenancy/v1/operator/tenants/{id}/activity", {
        params: { path: { id: tenantId }, query: { Page: paging.page, PageSize: paging.pageSize } },
        signal,
      }),
    ),
  );
  return (
    <>
      <ProblemAlert error={log.error ?? undefined} />
      <ActivityTable
        label={t("tenant.tabs.activity")}
        rows={log.data?.items.map((e) => ({
          id: e.id,
          occurredOn: e.occurredOn,
          kind: e.kind,
          who: e.actorName ?? e.actor,
          details: e.details,
        }))}
        totalCount={Number(log.data?.totalCount ?? 0)}
        page={paging.page}
        pageSize={paging.pageSize}
        onPage={(page, pageSize) => setPaging({ page, pageSize })}
      />
    </>
  );
}
