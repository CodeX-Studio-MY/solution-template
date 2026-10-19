import { expect, test } from "@playwright/test";
import { expectAccessible, mockApp, watchProblems } from "./support";

test("the shell starts with no business modules: home page, no CSP violations, accessible", async ({ page }) => {
  const problems = watchProblems(page);
  await mockApp(page);
  await page.goto("/");

  await expect(page.getByRole("heading", { name: "Welcome" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "No applications are available for your account." })).toBeVisible();

  await expectAccessible(page);
  expect(problems).toEqual([]);
});

test("a module that can't be loaded doesn't break the app", async ({ page }) => {
  await mockApp(page, {
    roles: ["broken:user"],
    modules: [{ id: "broken", entry: "/mfe/broken/0.0.0-dev/mf-manifest.json" }],
  });
  await page.goto("/");
  await expect(page.getByRole("heading", { name: "Welcome" })).toBeVisible();
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
  await page.goto("/somewhere/1");
  expect(new URL((await login).url()).searchParams.get("returnUrl")).toBe("/somewhere/1");
});
