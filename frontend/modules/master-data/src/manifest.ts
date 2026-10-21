import type { ModuleManifest, ModuleRoute } from "@farm360/web-core";
import { basePath, moduleKey } from "./basePath";
import en from "./locales/en.json";
import ms from "./locales/ms.json";
import zh from "./locales/zh.json";

/**
 * What the shell loads (`./manifest`). Master data is base data, not a workspace: it has no sidebar entry and no pages of its own,
 * only settings pages under the gear in the top bar (Settings → Master → Farm). Pages load lazily, only when visited.
 */
export default {
  id: moduleKey,
  basePath,
  routes: [] as ModuleRoute[],
  menu: [],
  settings: [
    {
      id: "farms",
      group: "settings.groups.master",
      label: "settings.farms",
      path: "farms",
      requiredRole: "user",
      lazy: () => import("./settings/FarmSettings"),
    },
  ],
  i18n: { namespace: moduleKey, resources: { en, ms, zh } },
} satisfies ModuleManifest;
