import { expect, test, type Page, type Route } from "@playwright/test";
import { expectAccessible, watchProblems } from "./support";

const json = (route: Route, body: unknown, status = 200) =>
  route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) });

const demo = {
  id: "44444444-4444-4444-4444-444444444444",
  slug: "demo",
  displayName: "Demo Co",
  status: "active",
  plan: "lite",
  planName: "Lite",
  modules: ["access-management", "tenant-settings", "billing"],
  deleteAfter: null,
  requireSupportApproval: true,
  timeZone: "Asia/Kuala_Lumpur",
  currency: "MYR",
  locale: "en",
};

const page_ = (items: unknown[]) => ({ items, pageNumber: 1, pageSize: 25, totalCount: items.length, totalPages: 1, hasNext: false });

const gateway = {
  principalId: "55555555-5555-5555-5555-555555555555",
  type: "device",
  displayName: "North Gate 04",
  isActive: true,
  identifier: "AC1F09FFFE0512A4",
  deviceType: "lorawanGateway",
  isExternal: null,
  tenantCount: 1,
};

const gatewayDetail = {
  ...gateway,
  email: null,
  manufacturer: "Dragino",
  model: "LPS8",
  ownerName: null,
  contactEmail: null,
  tenants: [
    { tenantId: demo.id, tenantSlug: demo.slug, tenantName: demo.displayName, status: "suspended", grantedOn: "2026-09-03T08:00:00Z" },
  ],
  providers: [],
  eTag: "1",
};

const aisha = {
  principalId: "66666666-6666-6666-6666-666666666666",
  type: "user",
  displayName: "Aisha Rahman",
  isActive: true,
  identifier: "aisha@example.com",
  deviceType: null,
  isExternal: null,
  tenantCount: 0,
};

const linked = (id: number, key: string, type: string, name: string, email: string | null, verified: boolean) => ({
  principalUserProviderId: id,
  identityProviderId: id,
  providerType: type,
  providerKey: key,
  providerName: name,
  providerEmail: email,
  providerEmailVerified: verified,
  linkedOn: "2026-10-01T08:00:00Z",
  lastUsedOn: id === 11 ? "2026-10-30T09:30:00Z" : null,
});

const gatewayTrail = [
  ["suspended", "suspended", "Gateway returned for repair"],
  ["granted", "active", null],
].map(([action, toStatus, reason], index) => ({
  id: index + 1,
  occurredOn: "2026-10-20T08:00:00Z",
  tenantId: demo.id,
  tenantName: demo.displayName,
  principalId: gateway.principalId,
  principalName: gateway.displayName,
  action,
  fromStatus: null,
  toStatus,
  actorId: "o1",
  actorName: "Ops One",
  reason,
  remoteAddress: null,
}));

const modules = [
  { moduleKey: "access-management", displayName: "Access Management" },
  { moduleKey: "billing", displayName: "Billing" },
  { moduleKey: "tenant-settings", displayName: "Tenant Settings" },
];

const plan = (
  planId: number,
  planKey: string,
  displayName: string,
  included: string[],
  tenantCount: number,
  monthlyPrice: number | null,
) => ({
  planId,
  planKey,
  displayName,
  description: null,
  monthlyPrice,
  currency: "MYR",
  sortOrder: planId,
  isActive: true,
  tenantCount,
  modules: included,
  eTag: `"p${planId}"`,
});

const provider = (id: number, key: string, type: string, name: string, extra: Record<string, unknown> = {}) => ({
  identityProviderId: id,
  providerType: type,
  providerKey: key,
  displayName: name,
  isEnabled: false,
  clientId: null as string | null,
  hasSecret: false,
  authority: null,
  appleTeamId: null,
  appleKeyId: null,
  callbackUrl: `https://login.localhost/account/external/${key}/callback`,
  linkedAccounts: 0,
  eTag: `"i${id}"`,
  ...extra,
});

/** The operator console on its own host, with the Gateway's operator BFF mocked. */
async function mockOperator(page: Page, calls: string[]) {
  let signedIn = false;
  let tenant = { ...demo };
  let operatorLocale: string | null = null;
  let accounts = [
    linked(11, "google", "google", "Google", "aisha@gmail.com", true),
    linked(12, "microsoft-acme", "microsoft", "Microsoft (Acme)", null, false),
  ];
  let plans = [
    plan(1, "lite", "Lite", ["access-management", "tenant-settings"], 12, 99),
    plan(2, "advanced", "Advanced", ["access-management", "tenant-settings", "billing"], 4, 299),
    plan(3, "complete", "Complete", ["access-management", "tenant-settings", "billing"], 1, null),
  ];
  let providers = [
    provider(1, "google", "google", "Google", {
      isEnabled: true,
      clientId: "client-1.apps.example.com",
      hasSecret: true,
      linkedAccounts: 3,
    }),
    provider(2, "microsoft", "microsoft", "Microsoft"),
    provider(3, "apple", "apple", "Apple"),
    provider(4, "facebook", "facebook", "Facebook"),
  ];
  await page.route(/\/(api|bff)\//, async (route) => {
    const request = route.request();
    const key = `${request.method()} ${new URL(request.url()).pathname}`;
    calls.push(key);
    if (key === "POST /bff/operator/login") {
      signedIn = request.headers()["x-csrf"] === "1";
      return route.fulfill({ status: signedIn ? 204 : 403 });
    }
    if (!signedIn) return route.fulfill({ status: 401 });
    if (key === "GET /api/tenancy/v1/operator/me")
      return json(route, { id: "o1", email: "ops@example.com", displayName: "Ops One", locale: operatorLocale });
    if (key === "PUT /api/tenancy/v1/operator/me/preferences") {
      operatorLocale = (request.postDataJSON() as { locale: string }).locale;
      return json(route, { id: "o1", email: "ops@example.com", displayName: "Ops One", locale: operatorLocale });
    }
    if (key === "GET /api/tenancy/v1/operator/plans") return json(route, plans);
    if (key === "GET /api/tenancy/v1/operator/modules") return json(route, modules);
    if (key === "POST /api/tenancy/v1/operator/plans") {
      const body = request.postDataJSON() as { planKey: string; displayName: string; monthlyPrice: number | null };
      const created = plan(plans.length + 1, body.planKey, body.displayName, [], 0, body.monthlyPrice);
      plans = [...plans, created];
      return json(route, created, 201);
    }
    const planModule = /^(PUT|DELETE) \/api\/tenancy\/v1\/operator\/plans\/(\d+)\/modules\/([a-z-]+)$/.exec(key);
    if (planModule) {
      const [, method, id, moduleKey] = planModule;
      plans = plans.map((p) =>
        String(p.planId) === id
          ? { ...p, modules: method === "PUT" ? [...p.modules, moduleKey!] : p.modules.filter((m) => m !== moduleKey) }
          : p,
      );
      return json(
        route,
        plans.find((p) => String(p.planId) === id),
      );
    }
    if (key === "GET /api/tenancy/v1/operator/identity-providers") return json(route, providers);
    const enabled = /^PUT \/api\/tenancy\/v1\/operator\/identity-providers\/(\d+)\/enabled$/.exec(key);
    if (enabled) {
      providers = providers.map((p) =>
        String(p.identityProviderId) === enabled[1] ? { ...p, isEnabled: (request.postDataJSON() as { enabled: boolean }).enabled } : p,
      );
      return json(
        route,
        providers.find((p) => String(p.identityProviderId) === enabled[1]),
      );
    }
    const edited = /^PUT \/api\/tenancy\/v1\/operator\/identity-providers\/(\d+)$/.exec(key);
    if (edited) {
      const body = request.postDataJSON() as { displayName: string; clientId: string; clientSecret: string };
      providers = providers.map((p) =>
        String(p.identityProviderId) === edited[1]
          ? { ...p, displayName: body.displayName, clientId: body.clientId || null, hasSecret: p.hasSecret || Boolean(body.clientSecret) }
          : p,
      );
      return json(
        route,
        providers.find((p) => String(p.identityProviderId) === edited[1]),
      );
    }
    if (key === "GET /api/tenancy/v1/operator/tenants") return json(route, [tenant]);
    if (key === `GET /api/tenancy/v1/operator/tenants/${demo.id}`) return json(route, tenant);
    if (key === "GET /api/tenancy/v1/operator/principals") return json(route, page_([gateway, aisha]));
    if (key === `GET /api/tenancy/v1/operator/principals/${aisha.principalId}`)
      return json(route, {
        ...aisha,
        email: "aisha@example.com",
        manufacturer: null,
        model: null,
        ownerName: null,
        contactEmail: null,
        tenants: [],
        providers: accounts,
        eTag: "2",
      });
    const unlinked = /^DELETE \/api\/tenancy\/v1\/operator\/principals\/[^/]+\/providers\/(\d+)$/.exec(key);
    if (unlinked) {
      accounts = accounts.filter((a) => String(a.principalUserProviderId) !== unlinked[1]);
      return route.fulfill({ status: 204 });
    }
    if (key === `GET /api/tenancy/v1/operator/principals/${gateway.principalId}`) return json(route, gatewayDetail);
    if (key === `GET /api/tenancy/v1/operator/principals/${gateway.principalId}/access-audit`) return json(route, page_(gatewayTrail));
    if (key === `POST /api/tenancy/v1/operator/tenants/${demo.id}/suspend`) {
      tenant = { ...tenant, status: "suspended" };
      return json(route, tenant);
    }
    return route.fulfill({ status: 404, contentType: "application/json", body: "{}" });
  });
}

test("an operator signs in, opens a tenant and suspends it after confirming", async ({ page, baseURL }) => {
  const problems = watchProblems(page);
  const calls: string[] = [];
  await mockOperator(page, calls);
  await page.goto(baseURL!.replace("//localhost", "//operator.localhost"));

  await expect(page.getByRole("heading", { level: 1, name: "Operator sign-in" })).toBeVisible();
  await expectAccessible(page);
  await page.getByLabel("Email").fill("ops@example.com");
  await page.getByLabel("Password").fill("secret");
  await page.getByLabel("Authenticator code").fill("123456");
  await page.getByRole("button", { name: "Sign in" }).click();

  await expect(page.getByRole("heading", { level: 1, name: "Tenants" })).toBeVisible();
  await expect(page.getByText("Ops One")).toBeVisible();
  await page.getByRole("button", { name: "Demo Co" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Demo Co" })).toBeVisible();

  await page.getByRole("button", { name: "Suspend" }).click();
  await page.getByRole("dialog", { name: "Suspend Demo Co?" }).getByRole("button", { name: "Suspend" }).click();
  await expect(page.getByText("Demo Co: Suspended.")).toBeVisible();
  await expect(page.getByRole("button", { name: "Resume" })).toBeVisible();
  expect(calls).toContain(`POST /api/tenancy/v1/operator/tenants/${demo.id}/suspend`);
  expect(problems.filter((p) => !p.includes("401"))).toEqual([]);
});

test("an operator finds a device, and its page shows the tenants it can reach and how that access changed", async ({ page, baseURL }) => {
  const problems = watchProblems(page);
  await mockOperator(page, []);
  await page.goto(baseURL!.replace("//localhost", "//operator.localhost"));
  await page.getByLabel("Email").fill("ops@example.com");
  await page.getByLabel("Password").fill("secret");
  await page.getByLabel("Authenticator code").fill("123456");
  await page.getByRole("button", { name: "Sign in" }).click();

  await page.getByRole("link", { name: "Principals" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Principals" })).toBeVisible();
  await expect(page.getByText("These records don't control sign-in yet.", { exact: false })).toBeVisible();
  await expect(page.getByRole("cell", { name: "LoRaWAN gateway" })).toBeVisible();
  await expectAccessible(page);

  await page.getByRole("button", { name: "North Gate 04" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "North Gate 04" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Access trail: Demo Co" })).toBeVisible();
  await expect(page.getByText("Gateway returned for repair")).toBeVisible();
  await expectAccessible(page);
  expect(problems.filter((p) => !p.includes("401"))).toEqual([]);
});

/** Axe reads colours mid-fade otherwise: wait until nothing is animating. */
const settled = (page: Page) => page.waitForFunction(() => document.getAnimations().length === 0);

async function signInToConsole(page: Page, baseURL: string) {
  await page.goto(baseURL.replace("//localhost", "//operator.localhost"));
  await page.getByLabel("Email").fill("ops@example.com");
  await page.getByLabel("Password").fill("secret");
  await page.getByLabel("Authenticator code").fill("123456");
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Tenants" })).toBeVisible();
}

test("an operator sees the plans against the modules, changes a module after a confirm and adds a plan", async ({ page, baseURL }) => {
  const problems = watchProblems(page);
  const calls: string[] = [];
  await mockOperator(page, calls);
  await signInToConsole(page, baseURL!);

  await page.getByRole("link", { name: "Subscriptions" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Subscriptions" })).toBeVisible();
  const table = page.getByRole("table", { name: "Modules included in each plan" });
  await expect(table.getByRole("columnheader", { name: /Lite/ })).toContainText("12 tenants");
  await expect(table.getByRole("columnheader", { name: /Complete/ })).toContainText("No price set");
  await expect(page.getByRole("checkbox", { name: "Billing in Lite" })).not.toBeChecked();
  await expect(page.getByRole("checkbox", { name: "Billing in Advanced" })).toBeChecked();
  await expectAccessible(page);

  await page.getByRole("checkbox", { name: "Billing in Lite" }).click();
  const confirm = page.getByRole("dialog", { name: "Add Billing to Lite?" });
  await expect(confirm).toContainText("12 tenants on Lite get Billing straight away.");
  await settled(page);
  await expectAccessible(page);
  await confirm.getByRole("button", { name: "Add module" }).click();
  await expect(page.getByText("Billing added to Lite.")).toBeVisible();
  await expect(page.getByRole("checkbox", { name: "Billing in Lite" })).toBeChecked();
  expect(calls).toContain("PUT /api/tenancy/v1/operator/plans/1/modules/billing");

  // Taking away what tenant admins use to manage their people needs a stronger warning.
  await page.getByRole("checkbox", { name: "Tenant Settings in Advanced" }).click();
  await expect(page.getByRole("dialog", { name: "Remove Tenant Settings from Advanced?" })).toContainText("they lose those screens");
  await page.getByRole("dialog").getByRole("button", { name: "Cancel" }).click();
  expect(calls).not.toContain("DELETE /api/tenancy/v1/operator/plans/2/modules/tenant-settings");

  await page.getByRole("button", { name: "Add plan" }).first().click();
  const form = page.getByRole("dialog", { name: "Add plan" });
  await form.getByLabel("Key").fill("starter");
  await form.getByLabel("Name").fill("Starter");
  await form.getByLabel("Monthly price").fill("49");
  await expectAccessible(page);
  await form.getByRole("button", { name: "Add plan" }).click();
  await expect(page.getByText("Starter added.")).toBeVisible();
  await expect(table.getByRole("columnheader", { name: /Starter/ })).toContainText("0 tenants");
  await expect(page.getByRole("checkbox", { name: "Billing in Starter" })).not.toBeChecked();
  expect(problems.filter((p) => !p.includes("401"))).toEqual([]);
});

test("on a phone the matrix scrolls sideways, the page does not, and the module column stays", async ({ page, baseURL }) => {
  await mockOperator(page, []);
  await page.setViewportSize({ width: 390, height: 800 });
  await signInToConsole(page, baseURL!);
  await page.getByRole("link", { name: "Subscriptions" }).click();
  await expect(page.getByRole("table", { name: "Modules included in each plan" })).toBeVisible();

  const geometry = await page.evaluate(() => {
    const table = document.querySelector("table")!;
    const scroller = table.parentElement!;
    scroller.scrollLeft = 120;
    const first = table.querySelector("tbody th")!.getBoundingClientRect();
    return {
      pageOverflows: document.documentElement.scrollWidth > document.documentElement.clientWidth,
      scrollerOverflows: scroller.scrollWidth > scroller.clientWidth,
      scrolled: scroller.scrollLeft,
      firstColumnLeft: first.left - scroller.getBoundingClientRect().left,
    };
  });
  expect(geometry.pageOverflows).toBe(false);
  expect(geometry.scrollerOverflows).toBe(true);
  expect(geometry.scrolled).toBeGreaterThan(0);
  expect(Math.abs(geometry.firstColumnLeft)).toBeLessThan(2);
  await expectAccessible(page);
});

test("an operator sees every sign-in provider, edits one without ever seeing its secret, and enables it", async ({ page, baseURL }) => {
  const problems = watchProblems(page);
  const calls: string[] = [];
  await mockOperator(page, calls);
  await signInToConsole(page, baseURL!);

  await page.getByRole("link", { name: "Sign-in providers" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Sign-in providers" })).toBeVisible();
  await expect(page.getByText("the login buttons aren't connected yet", { exact: false })).toBeVisible();
  const row = (name: string) => page.getByRole("row", { name: new RegExp(name) });
  await expect(row("Google")).toContainText("Enabled");
  await expect(row("Google")).toContainText("3");
  await expect(row("Microsoft")).toContainText("Disabled");
  await expectAccessible(page);

  await page.getByRole("button", { name: "Edit Microsoft" }).click();
  const dialog = page.getByRole("dialog", { name: "Edit Microsoft" });
  await expect(dialog.getByText("https://login.localhost/account/external/microsoft/callback")).toBeVisible();
  await expect(dialog.getByLabel("Authority")).toBeVisible();
  await expect(dialog.getByLabel("Team ID")).toHaveCount(0);
  await expect(dialog.getByText("No secret is stored yet.")).toBeVisible();
  await dialog.getByLabel("Client ID").fill("app-1");
  await dialog.getByLabel("Client secret").fill("never-shown-again");
  await expectAccessible(page);
  await dialog.getByRole("button", { name: "Save settings" }).click();
  await expect(page.getByText("Microsoft saved.")).toBeVisible();

  // Reopened: the secret is not in the page, only that one is stored.
  await page.getByRole("button", { name: "Edit Microsoft" }).click();
  await expect(
    page.getByRole("dialog", { name: "Edit Microsoft" }).getByText("A secret is stored. Leave this empty to keep it."),
  ).toBeVisible();
  expect(await page.content()).not.toContain("never-shown-again");
  await expect(page.getByLabel("Client secret")).toHaveValue("");
  await page.getByRole("dialog").getByRole("button", { name: "Cancel" }).click();

  await page.getByRole("button", { name: "Enable Microsoft" }).click();
  await expect(page.getByText("Microsoft is enabled.")).toBeVisible();
  expect(calls).toContain("PUT /api/tenancy/v1/operator/identity-providers/2/enabled");

  // Settings with linked accounts can only be disabled.
  await page.getByRole("button", { name: "Edit Google" }).click();
  await expect(page.getByRole("button", { name: "Delete Google" })).toBeDisabled();
  await expect(page.getByText("disable it instead of deleting it", { exact: false })).toBeVisible();
  expect(problems.filter((p) => !p.includes("401"))).toEqual([]);
});

test("a user's page lists each linked account and unlinks one after a confirm", async ({ page, baseURL }) => {
  const calls: string[] = [];
  await mockOperator(page, calls);
  await signInToConsole(page, baseURL!);
  await page.getByRole("link", { name: "Principals" }).click();
  await page.getByRole("button", { name: "Aisha Rahman" }).click();

  const accounts = page.getByRole("table", { name: "Linked sign-in accounts" });
  await expect(accounts.getByRole("row", { name: /Google/ })).toContainText("aisha@gmail.com");
  await expect(accounts.getByRole("row", { name: /Microsoft \(Acme\)/ })).toContainText("Never");
  await expectAccessible(page);

  await page.getByRole("button", { name: "Unlink Microsoft (Acme)" }).click();
  const dialog = page.getByRole("dialog", { name: "Unlink Microsoft (Acme) from Aisha Rahman?" });
  await expect(dialog).toContainText("Their other ways of signing in stay as they are.");
  await dialog.getByRole("button", { name: "Unlink" }).click();
  await expect(page.getByText("Microsoft (Acme) unlinked from Aisha Rahman.")).toBeVisible();
  await expect(page.getByRole("button", { name: "Unlink Microsoft (Acme)" })).toHaveCount(0);
  expect(calls).toContain("DELETE /api/tenancy/v1/operator/principals/66666666-6666-6666-6666-666666666666/providers/12");
});

test("an operator changes the console language, on the sign-in screen and after it, and it is saved only once signed in", async ({
  page,
  baseURL,
}) => {
  const calls: string[] = [];
  await mockOperator(page, calls);
  await page.goto(baseURL!.replace("//localhost", "//operator.localhost"));

  await expect(page.getByRole("heading", { level: 1, name: "Operator sign-in" })).toBeVisible();
  await page.getByRole("button", { name: "Language" }).click();
  await settled(page);
  await expectAccessible(page); // the menu open, with the current language marked
  await page.getByRole("menuitem", { name: "中文" }).click();
  await expect(page.getByRole("menu")).toBeHidden();
  await expect(page.getByRole("heading", { level: 1 })).not.toHaveText("Operator sign-in");
  await expect(page.locator("html")).toHaveAttribute("lang", "zh");
  expect(calls.filter((c) => c.includes("preferences"))).toEqual([]);
  await settled(page);
  await expectAccessible(page);

  // Back to English to sign in with the English labels, then choose Bahasa and see it saved.
  await page.getByRole("button", { name: "语言" }).click();
  await page.getByRole("menuitem", { name: "English" }).click();
  await signInToConsole(page, baseURL!);
  await page.getByRole("button", { name: "Language" }).click();
  const bahasa = new Intl.DisplayNames(["ms"], { type: "language" }).of("ms")!;
  await page.getByRole("menuitem", { name: bahasa }).click();
  await expect(page.getByRole("menu")).toBeHidden();
  await expect(page.getByRole("link", { name: "Langganan" })).toBeVisible();
  await expect(page.locator("html")).toHaveAttribute("lang", "ms");
  expect(calls).toContain("PUT /api/tenancy/v1/operator/me/preferences");
  await settled(page);
  await expectAccessible(page);
});
