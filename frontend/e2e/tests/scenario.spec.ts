import { expect, test, type Route } from "@playwright/test";
import {
  budget,
  costCenterBudgetEntry,
  costCenterField,
  expectAccessible,
  mockApp,
  purchaseRequestEntry,
  sampleRequest,
  type Scenario,
  watchProblems,
} from "./support";

/**
 * The reference scenario in the browser (SAAS_TEMPLATE_PROMPT 7.1 and 7.5): the same built shell and modules, two tenants,
 * and the only difference is the data each tenant's API returns.
 */
const json = (route: Route, body: unknown) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });

const demo: Scenario = {
  roles: ["purchase-request:manager"],
  modules: [{ id: "purchase-request", entry: purchaseRequestEntry }],
};

const demoPlus: Scenario = {
  roles: ["purchase-request:manager", "cost-center-budget:manager"],
  modules: [
    { id: "purchase-request", entry: purchaseRequestEntry },
    { id: "cost-center-budget", entry: costCenterBudgetEntry },
  ],
  branding: { primaryColor: "#aa3300" },
  features: { "purchase-request.draft-sharing": true },
  routes: {
    "GET /api/purchase-request/v1/custom-fields": (route) => json(route, [costCenterField]),
    [`GET /api/purchase-request/v1/requests/${sampleRequest.id}`]: (route) =>
      json(route, { ...sampleRequest, customFields: { "cost-center": "CC-100" } }),
    "GET /api/cost-center-budget/v1/budgets": (route) => json(route, [budget]),
    "GET /api/cost-center-budget/v1/budgets/CC-100": (route) => json(route, budget),
  },
};

test.describe("a demo user (Standard plan)", () => {
  test("sees no budget menu, no budget panel and no cost center field", async ({ page }) => {
    const problems = watchProblems(page);
    await mockApp(page, demo);

    await page.goto("/");
    await expect(page.getByRole("link", { name: "Purchase requests" }).first()).toBeVisible();
    await expect(page.getByRole("link", { name: "Cost center budgets" })).toHaveCount(0);

    await page.goto(`/purchase-request/${sampleRequest.id}`);
    await expect(page.getByRole("heading", { name: new RegExp(sampleRequest.number) })).toBeVisible();
    await expect(page.getByRole("heading", { name: /Budget of/ })).toHaveCount(0);

    await page.goto("/purchase-request/new");
    await expect(page.getByRole("heading", { name: "New purchase request" })).toBeVisible();
    await expect(page.getByLabel(/Cost center/)).toHaveCount(0);
    expect(problems).toEqual([]);
  });
});

test.describe("a demo-plus user (Plus plan, customised)", () => {
  test("sees the budget menu, the budget panel and the cost center field", async ({ page }) => {
    const problems = watchProblems(page);
    await mockApp(page, demoPlus);

    await page.goto("/");
    await expect(page.getByRole("link", { name: "Cost center budgets" }).first()).toBeVisible();

    // The panel comes from the other module, into a slot on the purchase request page.
    await page.goto(`/purchase-request/${sampleRequest.id}`);
    await expect(page.getByRole("heading", { name: "Budget of CC-100" })).toBeVisible();
    await expect(page.getByText(/8,500\.00/)).toBeVisible();
    await expect(page.getByText("Cost center:")).toBeVisible();
    await expectAccessible(page);

    await page.goto("/purchase-request/new");
    await expect(page.getByLabel(/Cost center/)).toBeVisible();
    await expect(page.getByLabel(/Cost center/)).toHaveAttribute("aria-required", "true");

    await page.goto("/cost-center-budget");
    await expect(page.getByRole("heading", { name: "Cost center budgets", level: 1 })).toBeVisible();
    await expect(page.getByRole("cell", { name: "Facilities" })).toBeVisible();
    await expectAccessible(page);
    expect(problems).toEqual([]);
  });

  test("a warning appears when the request is more than what is left in the budget", async ({ page }) => {
    await mockApp(page, {
      ...demoPlus,
      routes: {
        ...demoPlus.routes,
        [`GET /api/purchase-request/v1/requests/${sampleRequest.id}`]: (route) =>
          json(route, { ...sampleRequest, total: 9000, customFields: { "cost-center": "CC-100" } }),
      },
    });
    await page.goto(`/purchase-request/${sampleRequest.id}`);
    await expect(page.getByText("This request is more than what is left.")).toBeVisible();
  });
});

test("the theme colour differs per tenant, with the same code", async ({ page, browser }) => {
  await mockApp(page, demo);
  await page.goto("/purchase-request");
  const defaultColour = await page.getByRole("link", { name: "New request" }).evaluate((e) => getComputedStyle(e).backgroundColor);

  const other = await browser.newPage();
  await mockApp(other, demoPlus);
  await other.goto("/purchase-request");
  const brandedColour = await other.getByRole("link", { name: "New request" }).evaluate((e) => getComputedStyle(e).backgroundColor);

  expect(brandedColour).toBe("rgb(170, 51, 0)");
  expect(defaultColour).not.toBe(brandedColour);
  await other.close();
});

test("a failing cost-center-budget remote leaves the purchase request page working", async ({ page }) => {
  await mockApp(page, {
    ...demoPlus,
    modules: [
      { id: "purchase-request", entry: purchaseRequestEntry },
      { id: "cost-center-budget", entry: "/mfe/cost-center-budget/9.9.9/mf-manifest.json" }, // that version was never published
    ],
  });
  await page.goto(`/purchase-request/${sampleRequest.id}`);
  await expect(page.getByRole("heading", { name: new RegExp(sampleRequest.number) })).toBeVisible();
  await expect(page.getByRole("button", { name: "Approve" })).toBeVisible();
  await expect(page.getByRole("heading", { name: /Budget of/ })).toHaveCount(0);
  await expect(page.getByRole("link", { name: "Cost center budgets" })).toHaveCount(0);
});

test("a role-restricted menu is hidden for a user", async ({ page }) => {
  // Budgets are for finance (managers). A plain user of the module doesn't get the menu, and the API would refuse them anyway.
  await mockApp(page, { ...demoPlus, roles: ["purchase-request:user", "cost-center-budget:user"] });
  await page.goto("/");
  await expect(page.getByRole("link", { name: "Purchase requests" }).first()).toBeVisible();
  await expect(page.getByRole("link", { name: "Cost center budgets" })).toHaveCount(0);
});
