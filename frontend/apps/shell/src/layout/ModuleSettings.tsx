import { Link as RouterLink, Navigate, Outlet, useLocation } from "react-router";
import Tab from "@mui/material/Tab";
import Tabs from "@mui/material/Tabs";
import { useTranslation } from "react-i18next";
import { canAccess, useSession, type ModuleManifest } from "@saaskit/web-core";
import { EmptyState, PageHeader, ProblemAlert } from "@saaskit/ui-kit";
import { useConfigurableModules, useModuleName } from "../settings/api";
import { ModuleSettingsForm } from "../settings/ModuleSettingsForm";

/** What a person may configure in one module: its backend settings (General) and the settings pages its manifest adds. */
export function useModuleSettings(manifest: ModuleManifest) {
  const roles = useSession()?.moduleRoles ?? {};
  const configurable = useConfigurableModules();
  const view = configurable.data?.find((v) => v.moduleKey === manifest.id && v.settings.length > 0);
  const pages = (manifest.settings ?? []).filter((page) => canAccess(roles, manifest.id, page.requiredRole));
  return { view, pages, loading: configurable.isLoading, error: configurable.error, available: Boolean(view) || pages.length > 0 };
}

/** `/{module}/settings`: the module's settings, with a tab for General and for each page the module adds. */
export function ModuleSettings({ manifest }: { manifest: ModuleManifest }) {
  const { t } = useTranslation();
  const name = useModuleName()(manifest.id);
  const { pathname } = useLocation();
  const { view, pages, loading, error, available } = useModuleSettings(manifest);
  const base = `${manifest.basePath}/settings`;
  const current = pathname.slice(base.length).replace(/^\//, "");

  if (loading) return null;
  const allowed = current === "" ? available : pages.some((page) => page.path === current);
  return (
    <>
      <PageHeader title={t("common:moduleSettings.title", { name })} />
      <ProblemAlert error={error ?? undefined} />
      {(view ? 1 : 0) + pages.length > 1 ? (
        <Tabs value={current} aria-label={t("common:moduleSettings.tabs")} sx={{ mb: 3 }}>
          {view ? <Tab value="" label={t("common:moduleSettings.general")} component={RouterLink} to={base} /> : null}
          {pages.map((page) => (
            <Tab
              key={page.id}
              value={page.path}
              label={t(page.label, { ns: manifest.i18n.namespace })}
              component={RouterLink}
              to={`${base}/${page.path}`}
            />
          ))}
        </Tabs>
      ) : null}
      {allowed ? <Outlet /> : <EmptyState title={t("common:moduleSettings.none")} />}
    </>
  );
}

/** The General tab: the backend's settings for the module, or the first page when the module has no backend settings. */
export function ModuleGeneralSettings({ manifest }: { manifest: ModuleManifest }) {
  const { view, pages } = useModuleSettings(manifest);
  if (view) return <ModuleSettingsForm view={view} />;
  return pages[0] ? <Navigate to={pages[0].path} replace /> : null;
}
