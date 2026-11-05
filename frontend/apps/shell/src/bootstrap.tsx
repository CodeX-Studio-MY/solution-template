import { StrictMode, useEffect, useState } from "react";
import { CacheProvider } from "@emotion/react";
import { createRoot } from "react-dom/client";
import Alert from "@mui/material/Alert";
import CircularProgress from "@mui/material/CircularProgress";
import { api, platformDefaults, unwrap, useSessionStore, useTenantStore, type ModuleManifest } from "@saaskit/web-core";
import { App } from "./App";
import { emotionCache } from "./emotionCache";
import { setupI18n } from "./i18n/setup";
import { loadModules, type ModuleFailure } from "./modules/loader";

/** The language this person chose, or null when they have not (or it can't be read: the tenant's language applies then). */
async function loadPreferredLocale(): Promise<string | null> {
  try {
    const { locale } = await unwrap(api("identity").GET("/api/identity/v1/me/preferences"));
    return locale && (platformDefaults.locales as readonly string[]).includes(locale) ? locale : null;
  } catch {
    return null;
  }
}

/**
 * Start-up, in order: who is signed in (a 401 goes to sign-in), the tenant, the person's language, then the tenant's modules.
 * Modules that fail to load are listed in the console and skipped; the rest of the app runs.
 */
async function bootstrap(): Promise<{ manifests: ModuleManifest[]; failures: ModuleFailure[] }> {
  await useSessionStore.getState().load();
  await useTenantStore.getState().load();
  // Language order: the person's, then the tenant's, then the platform's. Formats follow the same choice.
  const preferred = await loadPreferredLocale();
  await setupI18n(preferred ?? useTenantStore.getState().tenant.locale);
  if (preferred) useTenantStore.getState().setLocale(preferred);
  const { modules, failures } = await loadModules();
  for (const failure of failures) console.error(`Module "${failure.id}" could not be loaded`, failure.error);
  return { manifests: modules.map((m) => m.manifest), failures };
}

function Root() {
  const [state, setState] = useState<{ manifests: ModuleManifest[] } | { error: Error } | null>(null);
  useEffect(() => {
    bootstrap().then(
      ({ manifests }) => setState({ manifests }),
      (error: Error) => setState({ error }),
    );
  }, []);
  if (state === null) return <CircularProgress aria-label="Loading" sx={{ m: 4 }} />;
  if ("error" in state)
    return (
      <Alert severity="error" role="alert">
        {state.error.message}
      </Alert>
    );
  return <App manifests={state.manifests} />;
}

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <CacheProvider value={emotionCache}>
      <Root />
    </CacheProvider>
  </StrictMode>,
);
