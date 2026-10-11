import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { beforeAll, beforeEach, describe, expect, it } from "vitest";
import { registerExtensions, unregisterExtensions, type ModuleManifest } from "@acme/web-core";
import { setTenantForTest, setupI18nForTest, signInForTest } from "@acme/web-core/testing";
import common from "../i18n/common.en.json";
import { Home } from "./Home";

const orders = {
  id: "purchase-request",
  basePath: "/purchase-request",
  routes: [],
  menu: [{ label: "menu", path: "", icon: "ReceiptLong", requiredRole: "user" }],
  i18n: { namespace: "purchase-request" },
} satisfies ModuleManifest;

const budgets = {
  id: "cost-center-budget",
  basePath: "/cost-center-budget",
  routes: [],
  menu: [{ label: "menu", path: "", icon: "Savings", requiredRole: "manager" }],
  i18n: { namespace: "cost-center-budget" },
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
      "purchase-request": { en: { menu: "Purchase requests" } },
      "cost-center-budget": { en: { menu: "Cost center budgets" } },
    });
  });

  beforeEach(() => {
    unregisterExtensions("purchase-request");
    setTenantForTest();
  });

  it("links to each application the person has a role in, and only those", () => {
    signInForTest(["purchase-request:user", "cost-center-budget:user"]);
    renderHome([orders, budgets]);

    expect(screen.getByRole("heading", { level: 1, name: "Welcome" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Purchase requests" })).toHaveAttribute("href", "/purchase-request");
    // The budget menu entry needs the manager role, which this person doesn't have.
    expect(screen.queryByRole("link", { name: "Cost center budgets" })).not.toBeInTheDocument();
  });

  it("has no 'At a glance' section when no module contributes a card", () => {
    signInForTest(["purchase-request:user"]);
    renderHome([orders]);
    expect(screen.queryByRole("heading", { name: "At a glance" })).not.toBeInTheDocument();
  });

  it("shows the cards that modules contribute to the home slot", async () => {
    signInForTest(["purchase-request:user"]);
    registerExtensions("purchase-request", [
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
});
