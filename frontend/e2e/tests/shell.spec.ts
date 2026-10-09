import { expect, test } from "@playwright/test";
import { expectAccessible, mockApp, purchaseRequestEntry, sampleRequest, watchProblems } from "./support";

test("the shell composes the tenant's module: menu, list, and no CSP violations", async ({ page }) => {
  const problems = watchProblems(page);
  await mockApp(page);
  await page.goto("/");

  await expect(page.getByRole("heading", { name: "Welcome" })).toBeVisible();
  await page.getByRole("link", { name: "Purchase requests" }).first().click();
  await expect(page).toHaveURL(/\/purchase-request$/);
  await expect(page.getByRole("heading", { name: "Purchase requests", level: 1 })).toBeVisible();
  await expect(page.getByRole("link", { name: sampleRequest.number })).toBeVisible();
  await expect(page.getByText("Waiting for approval")).toBeVisible();

  await expectAccessible(page);
  expect(problems).toEqual([]);
});

test("a person without a role in the module doesn't see it", async ({ page }) => {
  await mockApp(page, { roles: [] });
  await page.goto("/");
  await expect(page.getByRole("heading", { name: "No applications are available for your account." })).toBeVisible();
  await expect(page.getByRole("link", { name: "Purchase requests" })).toHaveCount(0);
  // The URL doesn't open it either (the API would refuse anyway).
  await page.goto("/purchase-request");
  await expect(page).toHaveURL(/\/$/);
});

test("a module that can't be loaded doesn't break the app", async ({ page }) => {
  await mockApp(page, {
    roles: ["purchase-request:user", "broken:user"],
    modules: [
      { id: "purchase-request", entry: purchaseRequestEntry },
      { id: "broken", entry: "/mfe/broken/0.0.0-dev/mf-manifest.json" },
    ],
  });
  await page.goto("/");
  await expect(page.getByRole("heading", { name: "Welcome" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Purchase requests" }).first()).toBeVisible();
});

test("an unknown address shows the 404 page inside the layout", async ({ page }) => {
  await mockApp(page);
  await page.goto("/nothing/here");
  await expect(page.getByText("We couldn't find that page.")).toBeVisible();
  await expect(page.getByRole("link", { name: "Home" }).first()).toBeVisible();
  await expectAccessible(page);
});

test("a session that has ended goes to sign-in, remembering where you were", async ({ page }) => {
  await mockApp(page, { signedIn: false });
  const login = page.waitForRequest(/\/bff\/login\?returnUrl=/);
  await page.goto("/purchase-request");
  expect(new URL((await login).url()).searchParams.get("returnUrl")).toBe("/purchase-request");
});

test("the tenant's branding colour reaches the module", async ({ page }) => {
  await mockApp(page, { branding: { primaryColor: "#aa3300" } });
  await page.goto("/purchase-request");
  const button = page.getByRole("link", { name: "New request" });
  await expect(button).toBeVisible();
  await expect(button).toHaveCSS("background-color", "rgb(170, 51, 0)");
});
