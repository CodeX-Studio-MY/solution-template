import { Outlet, type RouteObject } from "react-router";
import { ModuleBoundary } from "../layout/ModuleBoundary";
import { SettingsLayout } from "./SettingsLayout";
import type { SettingsSection } from "./sections";

/** `/settings/*`: the layout, then one lazy route per section. A module's page runs inside its own error boundary. */
export function settingsRoute(sections: readonly SettingsSection[]): RouteObject {
  return {
    path: "settings",
    element: <SettingsLayout sections={sections} />,
    children: [
      { index: true, element: null },
      ...sections.map((section) =>
        section.ns === "settings"
          ? { path: section.path, lazy: section.lazy }
          : {
              path: section.path,
              element: (
                <ModuleBoundary moduleId={String(section.module)}>
                  <Outlet />
                </ModuleBoundary>
              ),
              children: [{ index: true, lazy: section.lazy }],
            },
      ),
    ],
  };
}
