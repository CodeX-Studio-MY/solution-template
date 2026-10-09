import type { ModuleManifest } from "@acme/web-core";
import { basePath, moduleKey } from "./basePath";
import en from "./locales/en.json";
import ms from "./locales/ms.json";
import zh from "./locales/zh.json";

/** What the shell loads (`./manifest`): the pages, the menu entry and the strings. Pages load lazily, only when visited. */
export default {
  id: moduleKey,
  basePath,
  routes: [{ index: true, lazy: () => import("./pages/ItemList") }],
  menu: [{ label: "menu", path: "", icon: "ListAlt", requiredRole: "user" }],
  i18n: { namespace: moduleKey, resources: { en, ms, zh } },
} satisfies ModuleManifest;
