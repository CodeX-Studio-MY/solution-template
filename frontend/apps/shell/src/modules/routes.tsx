import type { RouteObject } from "react-router";
import { Navigate, Outlet } from "react-router";
import { RequireModuleRole, type ModuleManifest, type ModuleRoute } from "@saaskit/web-core";
import { ModuleBoundary } from "../layout/ModuleBoundary";

function toRoute(route: ModuleRoute): RouteObject {
  const base = { lazy: route.lazy };
  if (route.index) return { ...base, index: true };
  return { ...base, path: route.path, children: route.children?.map(toRoute) } as RouteObject;
}

/**
 * Turns the loaded manifests into routes: each module lives under its `basePath`, inside its own error boundary and Suspense,
 * so one failing module never breaks the app. A module's pages are shown only to people with a role in that module.
 */
export function moduleRoutes(manifests: readonly ModuleManifest[]): RouteObject[] {
  return manifests.map((manifest) => ({
    path: manifest.basePath.slice(1),
    element: (
      <ModuleBoundary moduleId={manifest.id}>
        <RequireModuleRole module={manifest.id} role="user" fallback={<Navigate to="/" replace />}>
          <Outlet />
        </RequireModuleRole>
      </ModuleBoundary>
    ),
    children: manifest.routes.map(toRoute),
  }));
}
