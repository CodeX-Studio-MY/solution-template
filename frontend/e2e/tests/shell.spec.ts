import { expect, test } from "@playwright/test";
import { expectAccessible, mockApp, watchProblems } from "./support";

test("a person with no module roles gets the empty home page: no CSP violations, accessible", async ({ page }) => {
  const problems = watchProblems(page);
  await mockApp(page);
  await page.goto("/");

  await expect(page.getByRole("heading", { name: "Welcome" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "No modules are available for your account." })).toBeVisible();

  await expectAccessible(page);
  expect(problems).toEqual([]);
});

test("Base ships with the template: its menu entry opens its empty page, translated, accessible, with no CSP violations", async ({
  page,
}) => {
  const problems = watchProblems(page);
  await mockApp(page, { roles: ["base:user"], modules: [{ id: "base", entry: "/mfe/base/0.0.0-dev/mf-manifest.json" }] });
  await page.goto("/");

  await page.getByRole("navigation").getByRole("link", { name: "Base" }).first().click();
  await expect(page).toHaveURL(/\/base$/);
  await expect(page.getByRole("heading", { level: 1, name: "Base" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "No master data yet" })).toBeVisible();
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

test("a saved language applies from the first screen, a switch is saved, and a refused save says so but keeps the language", async ({
  page,
}) => {
  let saved: string | null = "ms";
  let refuse = false;
  const body = () => JSON.stringify({ locale: saved });
  await mockApp(page, {
    routes: {
      "GET /api/identity/v1/me/preferences": (route) => route.fulfill({ status: 200, contentType: "application/json", body: body() }),
      "PUT /api/identity/v1/me/preferences": (route) => {
        if (refuse) return route.fulfill({ status: 403, contentType: "application/json", body: JSON.stringify({ message: "Read-only." }) });
        saved = (route.request().postDataJSON() as { locale: string }).locale;
        return route.fulfill({ status: 200, contentType: "application/json", body: body() });
      },
    },
  });
  await page.goto("/");

  // The preference decides the language before anything is drawn, not the tenant's.
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Selamat datang");
  await expect(page.locator("html")).toHaveAttribute("lang", "ms");
  await expectAccessible(page);

  await page.getByRole("button", { name: "Bahasa" }).click();
  await page.getByRole("menuitem", { name: "English" }).click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Welcome");
  await expect(page.locator("html")).toHaveAttribute("lang", "en");
  await expect.poll(() => saved).toBe("en");

  // A new page load (a new browser session) keeps it.
  await page.reload();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Welcome");

  refuse = true;
  await page.getByRole("button", { name: "Language" }).click();
  await page.getByRole("menuitem", { name: new Intl.DisplayNames(["zh"], { type: "language" }).of("zh")! }).click();
  // The message is in the language just chosen: the switch is real even though saving failed.
  await expect(page.getByText("语言已暂时更改，但未能保存。")).toBeVisible();
  await expect(page.locator("html")).toHaveAttribute("lang", "zh");
});
