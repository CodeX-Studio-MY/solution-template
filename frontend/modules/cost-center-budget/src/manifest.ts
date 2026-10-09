import type { ModuleManifest } from "@acme/web-core";
import { basePath, moduleKey } from "./basePath";
import en from "./locales/en.json";
import ms from "./locales/ms.json";
import zh from "./locales/zh.json";

/**
 * What the shell loads. This module is an add-on: it exists only for tenants whose plan includes it. It knows nothing about the
 * purchase-request module's code; it contributes a panel to a slot that `@acme/web-core` declares, and that is all they share.
 */
export default {
  id: moduleKey,
  basePath,
  routes: [{ index: true, lazy: () => import("./pages/BudgetList") }],
  menu: [{ label: "menu", path: "", icon: "Savings", requiredRole: "manager" }],
  extensions: [
    {
      slot: "purchase-request.detail.side-panel",
      component: () => import("./slots/RemainingBudget"),
      requiredRole: "user",
    },
  ],
  i18n: { namespace: moduleKey, resources: { en, ms, zh } },
} satisfies ModuleManifest;
