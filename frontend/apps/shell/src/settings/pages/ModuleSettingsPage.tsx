import Paper from "@mui/material/Paper";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";
import { useTranslation } from "react-i18next";
import { EmptyState, PageHeader, ProblemAlert } from "@saaskit/ui-kit";
import { useManifests } from "../../modules/manifests";
import { useConfigurableModules, useModuleName } from "../api";
import { ModuleSettingsForm } from "../ModuleSettingsForm";

/** Branding has its own page. */
const ownPage = new Set(["tenant-settings"]);

/**
 * Platform services: the settings of modules that have no screens of their own (billing, for example). A module with a frontend
 * keeps its settings in the module, under Settings in its menu.
 */
export function Component() {
  const { t } = useTranslation("settings");
  const moduleName = useModuleName();
  const manifests = useManifests();
  const all = useConfigurableModules();
  const views = (all.data ?? []).filter(
    (view) => !ownPage.has(view.moduleKey) && view.settings.length > 0 && !manifests.some((m) => m.id === view.moduleKey),
  );

  return (
    <>
      <PageHeader title={t("sections.modules")} subtitle={t("modules.subtitle")} />
      <ProblemAlert error={all.error ?? undefined} />
      {all.data && views.length === 0 ? <EmptyState title={t("modules.empty")} /> : null}
      <Stack spacing={3}>
        {views.map((view) => (
          <Paper key={view.moduleKey} variant="outlined" sx={{ p: 2 }} component="section" aria-label={moduleName(view.moduleKey)}>
            <Typography variant="h3" component="h2" sx={{ mb: 2 }}>
              {moduleName(view.moduleKey)}
            </Typography>
            <ModuleSettingsForm view={view} />
          </Paper>
        ))}
      </Stack>
    </>
  );
}
