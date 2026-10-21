import { useTranslation } from "react-i18next";
import { unwrap, useApiMutation } from "@saaskit/web-core";
import { SettingsForm, type SettingsView } from "@saaskit/ui-kit";
import { humanize, tenancy, tenancyQueries, useCanChangeSettings } from "./api";

/**
 * One module's settings, built from the server's metadata. Labels come from the module's strings (`{module}:settings.{name}`)
 * or the setting's name. Read-only for people who may read but not change them.
 */
export function ModuleSettingsForm({ view }: { view: SettingsView }) {
  const { t } = useTranslation("settings");
  const canChange = useCanChangeSettings(view.moduleKey);
  const save = useApiMutation(
    (changes: Record<string, unknown>) =>
      unwrap(
        tenancy.PUT("/api/tenancy/v1/settings/{moduleKey}", { params: { path: { moduleKey: view.moduleKey } }, body: changes as never }),
      ),
    { invalidate: tenancyQueries, success: t("modules.saved") },
  );
  const labels = Object.fromEntries(
    view.settings.map((s) => [s.name, { label: t(`settings.${s.name}`, { ns: view.moduleKey, defaultValue: humanize(s.name) }) }]),
  );
  return (
    <SettingsForm
      view={view}
      labels={labels}
      readOnly={!canChange}
      onSave={(changes) => save.mutateAsync(changes)}
      saving={save.isPending}
      error={save.error}
    />
  );
}
