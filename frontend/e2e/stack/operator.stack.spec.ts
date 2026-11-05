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

test("an operator adds a plan with a module, onboards a tenant on it, and the tenant has that module and no other", async ({ page }) => {
  const operator = devOperator();
  const run = Date.now().toString(36);
  const planName = `Stack ${run}`;
  const slug = `stk-${run}`;
  await page.goto(`https://operator.localhost:${port}/`);
  await page.getByLabel("Email").fill(operator.email);
  await page.getByLabel("Password").fill(operator.password);
  await page.getByLabel("Authenticator code").fill(totp(operator.totpSecret));
  await page.getByRole("button", { name: "Sign in" }).click();

  await page.getByRole("link", { name: "Subscriptions" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Subscriptions" })).toBeVisible();
  await page.getByRole("button", { name: "Add plan" }).first().click();
  const add = page.getByRole("dialog", { name: "Add plan" });
  await add.getByLabel("Key").fill(`stack-${run}`);
  await add.getByLabel("Name").fill(planName);
  await add.getByLabel("Monthly price").fill("149");
  await add.getByRole("button", { name: "Add plan" }).click();
  await expect(page.getByText(`${planName} added.`)).toBeVisible();
  const table = page.getByRole("table", { name: "Modules included in each plan" });
  await expect(table.getByRole("columnheader", { name: new RegExp(planName) })).toContainText("0 tenants");

  await page.getByRole("checkbox", { name: `Access Management in ${planName}` }).click();
  const confirm = page.getByRole("dialog", { name: `Add Access Management to ${planName}?` });
  await expect(confirm).toContainText(`0 tenants on ${planName}`);
  await confirm.getByRole("button", { name: "Add module" }).click();
  await expect(page.getByText(`Access Management added to ${planName}.`)).toBeVisible();
  await expect(page.getByRole("checkbox", { name: `Access Management in ${planName}` })).toBeChecked();
  await expect(page.getByRole("checkbox", { name: `Billing in ${planName}` })).not.toBeChecked();

  // The new plan is offered straight away, from the database rather than a list in the build.
  await page.getByRole("link", { name: "Tenants" }).click();
  await page.getByRole("button", { name: "Onboard tenant" }).click();
  const onboard = page.getByRole("dialog", { name: "Onboard a tenant" });
  await onboard.getByRole("textbox", { name: /^Name/ }).fill(`Stack ${run} Co`);
  await onboard.getByLabel("Address").fill(slug);
  await onboard.getByRole("combobox", { name: "Plan" }).click();
  await page.getByRole("option", { name: planName }).click();
  await onboard.getByLabel("First administrator's email").fill(`admin@${slug}.example`);
  await onboard.getByLabel("First administrator's name").fill("Stack Admin");
  await onboard.getByRole("button", { name: "Onboard", exact: true }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Close" }).click();

  await expect(page.getByRole("row", { name: new RegExp(`Stack ${run} Co.*${planName}`) })).toBeVisible();
  await page.getByRole("button", { name: `Stack ${run} Co` }).click();
  await expect(page.getByRole("combobox", { name: "Plan" })).toHaveText(planName);
  await expect(page.getByRole("checkbox", { name: "Access Management (In the plan)" })).toBeChecked();
  await expect(page.getByRole("checkbox", { name: "Billing" })).not.toBeChecked();
  await expect(page.getByRole("checkbox", { name: "Base" })).not.toBeChecked();

  // Back in Subscriptions the plan counts its tenant, and retiring it needs a confirm.
  await page.getByRole("link", { name: "Subscriptions" }).click();
  await expect(table.getByRole("columnheader", { name: new RegExp(planName) })).toContainText("1 tenant");
  await page.getByRole("button", { name: `Edit ${planName}` }).click();
  await page.getByRole("button", { name: "Retire plan" }).click();
  await page
    .getByRole("dialog", { name: `Retire ${planName}?` })
    .getByRole("button", { name: "Retire plan" })
    .click();
  await expect(page.getByText(`${planName} is retired.`)).toBeVisible();
  await expect(table.getByRole("columnheader", { name: new RegExp(planName) })).toContainText("Retired");
});

test("an operator adds Microsoft settings that can't be enabled until complete, never sees the secret again, then removes them", async ({
  page,
}) => {
  const operator = devOperator();
  const run = Date.now().toString(36);
  const name = `Microsoft ${run}`;
  await page.goto(`https://operator.localhost:${port}/`);
  await page.getByLabel("Email").fill(operator.email);
  await page.getByLabel("Password").fill(operator.password);
  await page.getByLabel("Authenticator code").fill(totp(operator.totpSecret));
  await page.getByRole("button", { name: "Sign in" }).click();

  await page.getByRole("link", { name: "Sign-in providers" }).click();
  const table = page.getByRole("table", { name: "Sign-in providers" });
  for (const seeded of ["Google", "Microsoft", "Apple", "Facebook"])
    await expect(table.getByRole("row", { name: new RegExp(seeded) }).first()).toBeVisible();

  await page.getByRole("button", { name: "Add provider" }).click();
  const add = page.getByRole("dialog", { name: "Add sign-in provider" });
  await add.getByRole("combobox", { name: "Provider" }).click();
  await page.getByRole("option", { name: "Microsoft" }).click();
  await add.getByLabel("Key").fill(`microsoft-${run}`);
  await add.getByRole("textbox", { name: /^Name/ }).fill(name);
  await add.getByRole("button", { name: "Add provider" }).click();
  await expect(page.getByText(`${name} added.`)).toBeVisible();
  await expect(table.getByRole("row", { name: new RegExp(name) })).toContainText("Disabled");

  // Nothing to sign in with yet, so the server refuses to enable it.
  await page.getByRole("button", { name: `Enable ${name}` }).click();
  await expect(page.getByText("Fill in all the settings this provider needs before enabling it.", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Dismiss" }).click();
  await expect(table.getByRole("row", { name: new RegExp(name) })).toContainText("Disabled");

  await page.getByRole("button", { name: `Edit ${name}` }).click();
  const edit = page.getByRole("dialog", { name: `Edit ${name}` });
  await expect(edit.getByText(new RegExp(`https://login\\.localhost(:\\d+)?/account/external/microsoft-${run}/callback`))).toBeVisible();
  await edit.getByLabel("Client ID").fill("stack-client");
  await edit.getByLabel("Client secret").fill("stack-secret-never-shown");
  await edit.getByLabel("Authority").fill("organizations");
  await edit.getByRole("button", { name: "Save settings" }).click();
  await expect(page.getByText(`${name} saved.`)).toBeVisible();
  expect(await page.content()).not.toContain("stack-secret-never-shown");

  await page.getByRole("button", { name: `Enable ${name}` }).click();
  await expect(page.getByText(`${name} is enabled.`)).toBeVisible();
  await page.getByRole("button", { name: `Disable ${name}` }).click();
  await page
    .getByRole("dialog", { name: `Disable ${name}?` })
    .getByRole("button", { name: "Disable" })
    .click();
  await expect(page.getByText(`${name} is disabled.`)).toBeVisible();

  await page.getByRole("button", { name: `Edit ${name}` }).click();
  await expect(page.getByText("A secret is stored. Leave this empty to keep it.")).toBeVisible();
  await page.getByRole("button", { name: `Delete ${name}` }).click();
  await page
    .getByRole("dialog", { name: `Delete ${name}?` })
    .getByRole("button", { name: "Delete" })
    .click();
  await expect(page.getByText(`${name} deleted.`)).toBeVisible();
  await expect(table.getByRole("row", { name: new RegExp(name) })).toHaveCount(0);
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
