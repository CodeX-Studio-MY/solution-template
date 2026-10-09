import { StrictMode, useEffect, useState } from "react";
import { CacheProvider } from "@emotion/react";
import { createRoot } from "react-dom/client";
import Alert from "@mui/material/Alert";
import CircularProgress from "@mui/material/CircularProgress";
import { useSessionStore, useTenantStore, type ModuleManifest } from "@acme/web-core";
import { App } from "./App";
import { emotionCache } from "./emotionCache";
import { setupI18n } from "./i18n/setup";
import { loadModules, type ModuleFailure } from "./modules/loader";

/**
 * Start-up, in order: who is signed in (a 401 goes to sign-in), the tenant, the language, then the tenant's modules.
 * Modules that fail to load are listed in the console and skipped; the rest of the app runs.
 */
async function bootstrap(): Promise<{ manifests: ModuleManifest[]; failures: ModuleFailure[] }> {
  await useSessionStore.getState().load();
  await useTenantStore.getState().load();
  await setupI18n(useTenantStore.getState().tenant.locale);
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
