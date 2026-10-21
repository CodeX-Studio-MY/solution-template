import { addNamespace, initI18n } from "@saaskit/web-core";
import { commonStrings } from "@saaskit/ui-kit";
import { settingsStrings } from "../settings/strings";

/** Starts i18n in the tenant's locale and registers the shared strings (`common`) and the Settings area's (`settings`). */
export async function setupI18n(locale: string): Promise<void> {
  await initI18n(locale);
  addNamespace("common", commonStrings);
  addNamespace("settings", settingsStrings);
}
