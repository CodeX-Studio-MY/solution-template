import { addNamespace, initI18n } from "@acme/web-core";
import en from "./common.en.json";
import ms from "./common.ms.json";
import zh from "./common.zh.json";

/** Starts i18n in the tenant's locale and registers the shell's own strings (namespace `common`). */
export async function setupI18n(locale: string): Promise<void> {
  await initI18n(locale);
  addNamespace("common", { en, ms, zh });
}
