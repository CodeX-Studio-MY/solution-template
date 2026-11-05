import { useMemo } from "react";
import Paper from "@mui/material/Paper";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";
import { useTranslation } from "react-i18next";
import { unwrap, useApiMutation, useApiQuery, useTenantStore } from "@saaskit/web-core";
import { PageHeader, ProblemAlert, StatusChip, TenantProfileForm, type TenantProfile } from "@saaskit/ui-kit";
import { tenancy, tenancyQueries, useCanManageTenant, useModuleName } from "../api";

/** The organisation's name, time zone, currency and language. Everyone in tenant-settings sees it; admins change it. */
export function Component() {
  const { t } = useTranslation("settings");
  const canEdit = useCanManageTenant();
  const moduleName = useModuleName();
  const tenant = useApiQuery(["tenancy", "me", "tenant"], (signal) => unwrap(tenancy.GET("/api/tenancy/v1/me/tenant", { signal })));
  const save = useApiMutation((body: TenantProfile) => unwrap(tenancy.PUT("/api/tenancy/v1/me/tenant/profile", { body })), {
    invalidate: tenancyQueries,
    success: t("profile.saved"),
    // The name, formats and language the whole app uses come from the tenant store: load it again.
    onSuccess: () => useTenantStore.getState().load(),
  });

  const data = tenant.data;
  const profile = useMemo<TenantProfile | undefined>(
    () => data && { displayName: data.displayName, timeZone: data.timeZone, currency: data.currency, locale: data.locale },
    [data],
  );
  return (
    <>
      <PageHeader title={t("sections.profile")} subtitle={t("profile.subtitle")} />
      <ProblemAlert error={tenant.error ?? undefined} />
      {data && profile ? (
        <Stack spacing={3}>
          <Paper variant="outlined" sx={{ p: 2 }}>
            <Stack direction={{ xs: "column", sm: "row" }} spacing={3}>
              <Fact label={t("profile.address")} value={data.slug} />
              <Fact label={t("profile.plan")} value={data.planName} />
              <div>
                <Typography variant="body2" color="text.secondary">
                  {t("profile.status")}
                </Typography>
                <StatusChip label={data.status} tone={data.status === "active" ? "success" : "warning"} />
              </div>
              <Fact label={t("profile.modules")} value={data.modules.map(moduleName).join(", ")} />
            </Stack>
          </Paper>
          <TenantProfileForm
            value={profile}
            onSave={(profile) => save.mutate(profile)}
            saving={save.isPending}
            error={save.error}
            disabled={!canEdit}
          />
        </Stack>
      ) : null}
    </>
  );
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <Typography variant="body2" color="text.secondary">
        {label}
      </Typography>
      <Typography>{value}</Typography>
    </div>
  );
}
