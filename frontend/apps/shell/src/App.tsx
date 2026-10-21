import { useMemo } from "react";
import { createBrowserRouter, RouterProvider, type RouteObject } from "react-router";
import CssBaseline from "@mui/material/CssBaseline";
import { ThemeProvider } from "@mui/material/styles";
import { CacheProvider } from "@emotion/react";
import { PlatformQueryProvider, useTenantSettings, type ModuleManifest } from "@saaskit/web-core";
import { createTenantTheme, FeedbackHost } from "@saaskit/ui-kit";
import { emotionCache } from "./emotionCache";
import { AppShell } from "./layout/AppShell";
import { Home } from "./pages/Home";
import { NotFound } from "./pages/NotFound";
import { moduleRoutes } from "./modules/routes";
import { ManifestsContext } from "./modules/manifests";
import { settingsRoute } from "./settings/routes";

/** The routes: the layout with the home page, the platform Settings, every module under its base path, and a 404. */
export function buildRoutes(manifests: readonly ModuleManifest[]): RouteObject[] {
  return [
    {
      path: "/",
      element: (
        <ManifestsContext value={manifests}>
          <AppShell manifests={manifests} />
        </ManifestsContext>
      ),
      children: [
        { index: true, element: <Home manifests={manifests} /> },
        settingsRoute(),
        ...moduleRoutes(manifests),
        { path: "*", element: <NotFound /> },
      ],
    },
  ];
}

/** The theme (with the tenant's branding), the style cache (with the CSP nonce), the query cache, snackbars and dialogs, and the router. */
export function App({ manifests }: { manifests: readonly ModuleManifest[] }) {
  const { branding } = useTenantSettings();
  const theme = useMemo(() => createTenantTheme(branding), [branding]);
  const router = useMemo(() => createBrowserRouter(buildRoutes(manifests)), [manifests]);
  return (
    <CacheProvider value={emotionCache}>
      <ThemeProvider theme={theme} defaultMode="light">
        <CssBaseline />
        <PlatformQueryProvider>
          <RouterProvider router={router} />
          <FeedbackHost />
        </PlatformQueryProvider>
      </ThemeProvider>
    </CacheProvider>
  );
}
