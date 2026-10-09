import i18next, { type i18n as I18n } from "i18next";
import { initReactI18next } from "react-i18next";
import { platformDefaults } from "../generated/platform";

/** Translations of one namespace, by locale. */
export type NamespaceResources = Partial<Record<string, Record<string, unknown>>>;

let instance: I18n | undefined;

/** Starts i18n once. The shell calls this with the tenant's locale; modules add their namespace later. */
export async function initI18n(locale: string = platformDefaults.locales[0]): Promise<I18n> {
  if (instance?.isInitialized) {
    if (instance.language !== locale) await instance.changeLanguage(locale);
    return instance;
  }
  instance = i18next.createInstance();
  await instance.use(initReactI18next).init({
    lng: locale,
    fallbackLng: platformDefaults.locales[0],
    supportedLngs: [...platformDefaults.locales],
    ns: [],
    defaultNS: "common",
    interpolation: { escapeValue: false }, // React escapes
    returnNull: false,
  });
  document.documentElement.lang = locale;
  return instance;
}

export function getI18n(): I18n {
  if (!instance) throw new Error("Call initI18n() before translating.");
  return instance;
}

/** A module (or the shell) adds its strings under its own namespace. Safe to call again. */
export function addNamespace(namespace: string, resources: NamespaceResources): void {
  const i18n = getI18n();
  for (const [locale, bundle] of Object.entries(resources)) {
    if (bundle) i18n.addResourceBundle(locale, namespace, bundle, true, true);
  }
}
