import Paper from "@mui/material/Paper";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";
import { useTranslation } from "react-i18next";
import { unwrap, useApiMutation, useApiQuery } from "@saaskit/web-core";
import { EmptyState, PageHeader, ProblemAlert, SettingsForm, type SettingsView } from "@saaskit/ui-kit";
import { humanize, tenancy, tenancyQueries, useModuleName } from "../api";

/** Branding has its own page; every other module's settings are here, one card each. */
const ownPage = new Set(["tenant-settings"]);

/** Each application's settings. Labels come from the module's strings (`{module}:settings.{name}`) or the setting's name. */
export function Component() {
  const { t } = useTranslation("settings");
  const all = useApiQuery(["tenancy", "settings"], (signal) => unwrap(tenancy.GET("/api/tenancy/v1/settings", { signal })));
  const views = (all.data ?? []).filter((view) => !ownPage.has(view.moduleKey) && view.settings.length > 0);

  return (
    <>
      <PageHeader title={t("sections.modules")} subtitle={t("modules.subtitle")} />
      <ProblemAlert error={all.error ?? undefined} />
      {all.data && views.length === 0 ? <EmptyState title={t("modules.empty")} /> : null}
      <Stack spacing={3}>
        {views.map((view) => (
          <ModuleCard key={view.moduleKey} view={view} />
        ))}
      </Stack>
    </>
  );
}

function ModuleCard({ view }: { view: SettingsView }) {
  const { t } = useTranslation("settings");
  const save = useApiMutation(
    (changes: Record<string, unknown>) =>
      unwrap(
        tenancy.PUT("/api/tenancy/v1/settings/{moduleKey}", { params: { path: { moduleKey: view.moduleKey } }, body: changes as never }),
      ),
    { invalidate: tenancyQueries, success: t("modules.saved") },
  );
  const name = useModuleName()(view.moduleKey);
  const labels = Object.fromEntries(
    view.settings.map((s) => [s.name, { label: t(`settings.${s.name}`, { ns: view.moduleKey, defaultValue: humanize(s.name) }) }]),
  );
  return (
    <Paper variant="outlined" sx={{ p: 2 }} component="section" aria-label={name}>
      <Typography variant="h3" component="h2" sx={{ mb: 2 }}>
        {name}
      </Typography>
      <SettingsForm
        view={view}
        labels={labels}
        onSave={(changes) => save.mutateAsync(changes)}
        saving={save.isPending}
        error={save.error}
      />
    </Paper>
  );
}
