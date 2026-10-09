import { useSessionStore, toSession } from "../session/sessionStore";
import { useTenantStore, type TenantSettings } from "../tenant/tenantStore";

/** Puts a signed-in person in the session store (for unit tests of components). */
export function signInForTest(roles: string[], overrides: { subject?: string; tenant?: string } = {}): void {
  useSessionStore
    .getState()
    .set(toSession({ subject: overrides.subject ?? "user-1", tenant: overrides.tenant ?? "demo", principalType: "user", roles }));
}

export function setTenantForTest(settings: Partial<TenantSettings> = {}): void {
  useTenantStore.setState({ status: "ready", settings: { branding: {}, features: {}, values: {}, ...settings } });
}

import { addNamespace, initI18n, type NamespaceResources } from "../i18n/i18n";

/** Starts i18n in English with the given namespaces (for unit tests of components that translate). */
export async function setupI18nForTest(namespaces: Record<string, NamespaceResources> = {}): Promise<void> {
  await initI18n("en");
  for (const [namespace, resources] of Object.entries(namespaces)) addNamespace(namespace, resources);
}
