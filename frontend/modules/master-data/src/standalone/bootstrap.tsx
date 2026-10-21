import { StrictMode, useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import { createBrowserRouter, RouterProvider } from "react-router";
import CssBaseline from "@mui/material/CssBaseline";
import { ThemeProvider } from "@mui/material/styles";
import { addNamespace, initI18n, useSessionStore, useTenantStore } from "@farm360/web-core";
import { createTenantTheme } from "@farm360/ui-kit";
import manifest from "../manifest";

/**
 * A lightweight dev shell so the module can run on its own (`pnpm dev` in this folder): a session from the Gateway it proxies
 * to, the tenant's language and theme, and just this module's routes and settings pages (at the same paths as in the shell).
 * The real shell does all of this and more.
 */
async function start() {
  await useSessionStore.getState().load();
  await useTenantStore.getState().load();
  await initI18n(useTenantStore.getState().tenant.locale);
  addNamespace("common", { en: { actions: { cancel: "Cancel" }, customFields: { title: "Additional information", none: "None" } } });
  if (manifest.i18n.resources) addNamespace(manifest.i18n.namespace, manifest.i18n.resources);
  const settings = (manifest.settings ?? []).map((page) => ({ path: `/settings${manifest.basePath}/${page.path}`, lazy: page.lazy }));
  const home = settings[0]?.path ?? manifest.basePath;
  return createBrowserRouter([
    { path: manifest.basePath, children: manifest.routes.map((r) => ({ ...r })) as never },
    ...settings,
    { path: "*", element: <a href={home}>{manifest.id}</a> },
  ]);
}

function Root() {
  const [router, setRouter] = useState<Awaited<ReturnType<typeof start>> | null>(null);
  useEffect(() => {
    void start().then(setRouter);
  }, []);
  if (!router) return null;
  return (
    <ThemeProvider theme={createTenantTheme()}>
      <CssBaseline />
      <RouterProvider router={router} />
    </ThemeProvider>
  );
}

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <Root />
  </StrictMode>,
);
