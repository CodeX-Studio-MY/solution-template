import type { RouteObject } from "react-router";
import { SettingsLayout } from "./SettingsLayout";
import { settingsSections } from "./sections";

/** `/settings/*`: the platform settings layout, then one lazy route per section. */
export function settingsRoute(): RouteObject {
  return {
    path: "settings",
    element: <SettingsLayout sections={settingsSections} />,
    children: [{ index: true, element: null }, ...settingsSections.map((section) => ({ path: section.path, lazy: section.lazy }))],
  };
}
