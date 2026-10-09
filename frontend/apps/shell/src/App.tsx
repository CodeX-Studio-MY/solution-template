import { useMemo } from "react";
import { createBrowserRouter, RouterProvider, type RouteObject } from "react-router";
import CssBaseline from "@mui/material/CssBaseline";
import { ThemeProvider } from "@mui/material/styles";
import { CacheProvider } from "@emotion/react";
import { useTenantSettings, type ModuleManifest } from "@acme/web-core";
import { createTenantTheme } from "@acme/ui-kit";
import { emotionCache } from "./emotionCache";
import { AppShell } from "./layout/AppShell";
import { Home } from "./pages/Home";
import { NotFound } from "./pages/NotFound";
import { moduleRoutes } from "./modules/routes";

/** The routes: the layout with the home page, every module under its base path, and a 404. */
export function buildRoutes(manifests: readonly ModuleManifest[]): RouteObject[] {
  return [
    {
      path: "/",
      element: <AppShell manifests={manifests} />,
      children: [
        { index: true, element: <Home manifests={manifests} /> },
        ...moduleRoutes(manifests),
        { path: "*", element: <NotFound /> },
      ],
    },
  ];
}

/** The theme (with the tenant's branding), the style cache (with the CSP nonce) and the router. */
export function App({ manifests }: { manifests: readonly ModuleManifest[] }) {
  const { branding } = useTenantSettings();
  const theme = useMemo(() => createTenantTheme(branding), [branding]);
  const router = useMemo(() => createBrowserRouter(buildRoutes(manifests)), [manifests]);
  return (
    <CacheProvider value={emotionCache}>
      <ThemeProvider theme={theme} defaultMode="light">
        <CssBaseline />
        <RouterProvider router={router} />
      </ThemeProvider>
    </CacheProvider>
  );
}
