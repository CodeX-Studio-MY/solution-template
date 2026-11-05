import { expect, test } from "@playwright/test";
import { acceptInvitation, origin, password } from "./support";

/** A person's language is saved on the server, so a brand-new browser session finds it. Against the real Identity service. */
test("the demo admin switches language, and a new browser session still has it", async ({ browser, page }) => {
  await acceptInvitation(browser, "demo");
  const own = (locale: string) => new Intl.DisplayNames([locale], { type: "language" }).of(locale)!;

  // Signs in whatever language the saved preference is in, and starts from English (an earlier failed run may have left another).
  await page.goto(`${origin("demo")}/`);
  await page.getByLabel("Email").fill("admin@demo.example");
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  const menu = page.getByRole("button", { name: /^(Language|Bahasa|语言)$/ });
  await expect(menu).toBeVisible({ timeout: 30_000 });
  if ((await page.locator("html").getAttribute("lang")) !== "en") {
    await menu.click();
    await page.getByRole("menuitem", { name: own("en") }).click();
  }
  await expect(page.getByRole("heading", { level: 1, name: "Welcome" })).toBeVisible();

  try {
    const saved = (locale: string) =>
      page.waitForResponse(
        (r) =>
          r.url().includes("/api/identity/v1/me/preferences") &&
          r.request().method() === "PUT" &&
          r.request().postData()?.includes(locale) === true,
      );
    await page.getByRole("button", { name: "Language" }).click();
    const savedMs = saved("ms");
    await page.getByRole("menuitem", { name: own("ms") }).click();
    expect((await savedMs).status()).toBe(200);
    await expect(page.getByRole("heading", { level: 1, name: "Selamat datang" })).toBeVisible();
    await expect(page.locator("html")).toHaveAttribute("lang", "ms");

    // A fresh context shares nothing with the first: no cookies, no local storage. Only the server remembers.
    const fresh = await browser.newContext({ ignoreHTTPSErrors: true });
    const second = await fresh.newPage();
    await second.goto(page.url());
    await second.getByLabel("Email").fill("admin@demo.example");
    await second.getByLabel("Password").fill(password);
    await second.getByRole("button", { name: "Sign in", exact: true }).click();
    await expect(second.getByRole("heading", { level: 1, name: "Selamat datang" })).toBeVisible({ timeout: 30_000 });
    await expect(second.locator("html")).toHaveAttribute("lang", "ms");
    await fresh.close();
  } finally {
    // Leave the demo tenant in English: the other stack tests look for English labels.
    await page.getByRole("button", { name: "Bahasa" }).click();
    const savedEn = page.waitForResponse((r) => r.url().includes("/api/identity/v1/me/preferences") && r.request().method() === "PUT");
    await page.getByRole("menuitem", { name: own("en") }).click();
    expect((await savedEn).status()).toBe(200);
    await expect(page.getByRole("heading", { level: 1, name: "Welcome" })).toBeVisible();
  }
});
