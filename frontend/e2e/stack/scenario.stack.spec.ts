import { expect, test } from "@playwright/test";
import { acceptInvitation, origin, signIn } from "./support";

/**
 * The reference scenario for real (SAAS_TEMPLATE_PROMPT 7.1): the same built app, two tenants, and what each sees is decided by data.
 * Needs the stack from scripts/dev-up. Each tenant's first admin accepts the invitation, signs in, and uses the app.
 */
test.describe.configure({ mode: "serial" });

test.beforeAll(async ({ browser }) => {
  await acceptInvitation(browser, "demo");
  await acceptInvitation(browser, "demo-plus");
});

test("demo (Standard plan): purchase requests with the defaults, and no budget module", async ({ page }) => {
  const problems: string[] = [];
  page.on("pageerror", (e) => problems.push(e.message));
  // The one 401 is the shell asking who is signed in before anyone is; that is how it learns to send people to sign in.
  page.on("console", (m) => m.type() === "error" && !m.text().includes("status of 401") && problems.push(m.text()));

  await signIn(page, "demo");
  await expect(page.getByRole("link", { name: "Purchase requests" }).first()).toBeVisible();
  await expect(page.getByRole("link", { name: "Cost center budgets" })).toHaveCount(0);

  await page.getByRole("link", { name: "Purchase requests" }).first().click();
  await expect(page.getByRole("heading", { name: "Purchase requests", level: 1 })).toBeVisible();
  await expect(page.getByRole("link", { name: /^PR-\d{8}-0001$/ })).toBeVisible();
  await expect(page.getByRole("cell", { name: "Laptops for new hires" })).toBeVisible();

  // The approval needs a manager; the admin is one, and isn't the requester, so the server offers the actions.
  await page.getByRole("cell", { name: "Laptops for new hires" }).locator("..").getByRole("link").click();
  await expect(page.getByRole("button", { name: "Approve" })).toBeVisible();
  await expect(page.getByRole("heading", { name: /Budget of/ })).toHaveCount(0);

  await page.goto(`${origin("demo")}/purchase-request/new`);
  await expect(page.getByRole("heading", { name: "New purchase request" })).toBeVisible();
  await expect(page.getByLabel(/Cost center/)).toHaveCount(0);
  expect(problems, problems.join("\n")).toEqual([]);
});

test("demo-plus (Plus plan, customised): its own colour, the cost center field and the budget module", async ({ page }) => {
  const problems: string[] = [];
  page.on("pageerror", (e) => problems.push(e.message));
  // The one 401 is the shell asking who is signed in before anyone is; that is how it learns to send people to sign in.
  page.on("console", (m) => m.type() === "error" && !m.text().includes("status of 401") && problems.push(m.text()));

  await signIn(page, "demo-plus");
  await expect(page.getByRole("link", { name: "Cost center budgets" }).first()).toBeVisible();

  // Branding: the tenant's own primary colour on the app bar.
  await expect(page.getByRole("banner")).toHaveCSS("background-color", "rgb(123, 63, 160)");

  // Rung 4: a required cost center, chosen from a list.
  await page.goto(`${origin("demo-plus")}/purchase-request/new`);
  await expect(page.getByLabel(/Cost center/)).toBeVisible();

  // Rung 6: the other module's panel on this module's page.
  await page.goto(`${origin("demo-plus")}/purchase-request`);
  await page.getByRole("cell", { name: "Standing desks" }).locator("..").getByRole("link").click();
  await expect(page.getByRole("heading", { name: "Budget of CC-100" })).toBeVisible();
  // Rung 2: the flag is on.
  await expect(page.getByRole("button", { name: "Copy link to draft" })).toHaveCount(0); // it's submitted, not a draft: nothing to share
  // Rung 3: two-level. The admin approves step 1; the request still waits (a different admin has to give step 2).
  // (A second run finds the first approval already given: the data is real, so this step is safe to repeat.)
  const approve = page.getByRole("button", { name: "Approve" });
  if (await approve.isVisible()) await approve.click();
  await expect(page.getByText("Approved (step 1)")).toBeVisible();
  await expect(page.getByText("Waiting for approval")).toBeVisible();

  // Rung 7: finance's budgets, with what approved requests have used.
  await page.goto(`${origin("demo-plus")}/cost-center-budget`);
  await expect(page.getByRole("heading", { name: "Cost center budgets", level: 1 })).toBeVisible();
  await expect(page.getByRole("cell", { name: "Facilities" })).toBeVisible();
  await expect(page.getByRole("row", { name: /CC-200/ })).toContainText("2,750.00"); // the approved "Monitors" request used 2,250 of 5,000
  expect(problems, problems.join("\n")).toEqual([]);
});

test("the two tenants are separate: demo-plus data isn't visible in demo", async ({ page }) => {
  await signIn(page, "demo");
  const response = await page.request.get(`${origin("demo")}/api/cost-center-budget/v1/budgets`);
  expect(response.status()).toBe(403); // not entitled
});
