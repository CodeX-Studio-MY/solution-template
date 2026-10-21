import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { beforeAll, beforeEach, describe, expect, it } from "vitest";
import { registerExtensions, unregisterExtensions, type ModuleManifest } from "@farm360/web-core";
import { setTenantForTest, setupI18nForTest, signInForTest } from "@farm360/web-core/testing";
import common from "../i18n/common.en.json";
import { Home } from "./Home";

const orders = {
  id: "goods-receipt",
  basePath: "/goods-receipt",
  routes: [],
  menu: [{ label: "menu", path: "", icon: "ReceiptLong", requiredRole: "user" }],
  i18n: { namespace: "goods-receipt" },
} satisfies ModuleManifest;

const budgets = {
  id: "supplier-rating",
  basePath: "/supplier-rating",
  routes: [],
  menu: [{ label: "menu", path: "", icon: "Savings", requiredRole: "manager" }],
  i18n: { namespace: "supplier-rating" },
} satisfies ModuleManifest;

function renderHome(manifests: ModuleManifest[]) {
  return render(
    <MemoryRouter>
      <Home manifests={manifests} />
    </MemoryRouter>,
  );
}

describe("Home", () => {
  beforeAll(async () => {
    await setupI18nForTest({
      common: { en: common },
      "goods-receipt": { en: { menu: "Goods receipts" } },
      "supplier-rating": { en: { menu: "Supplier ratings" } },
    });
  });

  beforeEach(() => {
    unregisterExtensions("goods-receipt");
    setTenantForTest();
  });

  it("links to each application the person has a role in, and only those", () => {
    signInForTest(["goods-receipt:user", "supplier-rating:user"]);
    renderHome([orders, budgets]);

    expect(screen.getByRole("heading", { level: 1, name: "Welcome" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Goods receipts" })).toHaveAttribute("href", "/goods-receipt");
    // The budget menu entry needs the manager role, which this person doesn't have.
    expect(screen.queryByRole("link", { name: "Supplier ratings" })).not.toBeInTheDocument();
  });

  it("has no 'At a glance' section when no module contributes a card", () => {
    signInForTest(["goods-receipt:user"]);
    renderHome([orders]);
    expect(screen.queryByRole("heading", { name: "At a glance" })).not.toBeInTheDocument();
  });

  it("shows the cards that modules contribute to the home slot", async () => {
    signInForTest(["goods-receipt:user"]);
    registerExtensions("goods-receipt", [
      { slot: "home.dashboard", component: async () => ({ default: () => <p>Two drafts</p> }), requiredRole: "user" },
    ]);
    renderHome([orders]);

    expect(screen.getByRole("heading", { name: "At a glance" })).toBeInTheDocument();
    expect(await screen.findByText("Two drafts")).toBeInTheDocument();
  });

  it("says so when the person has no application", () => {
    signInForTest([]);
    renderHome([orders]);
    expect(screen.getByText("No applications are available for your account.")).toBeInTheDocument();
  });

  it("points to Settings when the person only has settings pages", () => {
    signInForTest(["master-data:user"]);
    const masterData = {
      id: "master-data",
      basePath: "/master-data",
      routes: [],
      menu: [],
      settings: [
        {
          id: "farms",
          group: "groups.master",
          label: "farms",
          path: "farms",
          requiredRole: "user",
          lazy: () => Promise.resolve({ Component: () => null }),
        },
      ],
      i18n: { namespace: "master-data" },
    } satisfies ModuleManifest;
    renderHome([masterData]);
    expect(screen.getByText(common.home.settingsOnlyHint)).toBeInTheDocument();
  });
});
