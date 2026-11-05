import { expect, test } from "@playwright/test";
import { devOperator, port, totp } from "./support";

/** The operator console on its own host, through the Gateway's operator BFF, against the real Tenancy service. */
test("the development operator signs in, opens demo, and the session lives only in an HttpOnly cookie", async ({ page, context }) => {
  const operator = devOperator();
  await page.goto(`https://operator.localhost:${port}/`);
  await expect(page.getByRole("heading", { level: 1, name: "Operator sign-in" })).toBeVisible();
  await page.getByLabel("Email").fill(operator.email);
  await page.getByLabel("Password").fill(operator.password);
  await page.getByLabel("Authenticator code").fill(totp(operator.totpSecret));
  await page.getByRole("button", { name: "Sign in" }).click();

  await expect(page.getByRole("heading", { level: 1, name: "Tenants" })).toBeVisible();
  const cookie = (await context.cookies()).find((c) => c.name === "__Host-operator");
  expect(cookie?.httpOnly).toBe(true);
  expect(await page.evaluate(() => document.cookie)).not.toContain("__Host-operator");

  await page.getByRole("button", { name: /demo/i }).first().click();
  await expect(page.getByRole("tab", { name: "Overview" })).toBeVisible();
  await page.getByRole("tab", { name: "Features" }).click();
  await expect(page.getByRole("table", { name: "Features" })).toBeVisible();

  // A support request is recorded in the tenant's activity, under the operator's name.
  await page.getByRole("tab", { name: "Overview" }).click();
  await page.getByLabel("Reason").fill("Stack test: checking an import");
  await page.getByRole("button", { name: "Request" }).click();
  await expect(page.getByText(/Support access requested/)).toBeVisible();
  await page.getByRole("tab", { name: "Activity" }).click();
  await expect(
    page
      .getByRole("table", { name: "Activity" })
      .getByRole("row", { name: /Support access requested.*Dev Operator/ })
      .first(),
  ).toBeVisible();

  await page.getByRole("button", { name: "Sign out" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Operator sign-in" })).toBeVisible();
});

test("an operator adds a device, grants it access to demo, revokes it, and reads the trail", async ({ page }) => {
  const operator = devOperator();
  const identifier = `STACK${Date.now()}`;
  await page.goto(`https://operator.localhost:${port}/`);
  await page.getByLabel("Email").fill(operator.email);
  await page.getByLabel("Password").fill(operator.password);
  await page.getByLabel("Authenticator code").fill(totp(operator.totpSecret));
  await page.getByRole("button", { name: "Sign in" }).click();

  await page.getByRole("link", { name: "Principals" }).click();
  await page.getByRole("button", { name: "Add principal" }).click();
  const add = page.getByRole("dialog", { name: "Add a principal" });
  await add.getByRole("combobox", { name: "Type" }).click();
  await page.getByRole("option", { name: "Device" }).click();
  await add.getByRole("textbox", { name: "Name" }).fill(`Gateway ${identifier}`);
  await add.getByRole("textbox", { name: "Identifier" }).fill(identifier);
  await add.getByRole("button", { name: "Add principal" }).click();
  await expect(page.getByRole("heading", { level: 1, name: `Gateway ${identifier}` })).toBeVisible();

  await page.getByRole("link", { name: "Tenants" }).click();
  await page.getByRole("button", { name: /demo/i }).first().click();
  await page.getByRole("tab", { name: "Access" }).click();
  await page.getByRole("button", { name: "Grant access" }).click();
  const grant = page.getByRole("dialog", { name: "Grant access to this tenant" });
  await grant.getByRole("combobox", { name: "Principal" }).fill(identifier);
  await page.getByRole("option", { name: new RegExp(identifier) }).click();
  await grant.getByRole("button", { name: "Grant access" }).click();
  await expect(page.getByRole("link", { name: `Gateway ${identifier}` })).toBeVisible();

  await page.getByRole("button", { name: `Revoke Gateway ${identifier}` }).click();
  const revoke = page.getByRole("dialog", { name: `Revoke access for Gateway ${identifier}?` });
  await revoke.getByRole("textbox", { name: /Reason/ }).fill("Stack test: gateway returned");
  await revoke.getByRole("button", { name: "Revoke access" }).click();
  await expect(page.getByRole("button", { name: `Grant Gateway ${identifier} access again` })).toBeVisible();

  await page.getByRole("link", { name: `Gateway ${identifier}` }).click();
  await expect(page.getByRole("heading", { name: /Access trail/ })).toBeVisible();
  await expect(page.getByText("Stack test: gateway returned")).toBeVisible();
  await expect(page.getByRole("listitem").filter({ hasText: "Revoked" })).toContainText("Dev Operator");
});

test("the operator console isn't served on a tenant host, and the tenant app isn't served on the operator host", async ({ request }) => {
  const tenant = await request.get(`https://demo.localhost:${port}/api/tenancy/v1/operator/tenants`, { ignoreHTTPSErrors: true });
  expect(tenant.status()).toBe(401);
  const login = await request.post(`https://demo.localhost:${port}/bff/operator/login`, {
    headers: { "X-CSRF": "1" },
    data: {},
    ignoreHTTPSErrors: true,
  });
  expect(login.status()).toBe(404);
});
