import { expect, test, type Page, type Route } from "@playwright/test";
import { expectAccessible, watchProblems } from "./support";

const json = (route: Route, body: unknown, status = 200) =>
  route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) });

const demo = {
  id: "44444444-4444-4444-4444-444444444444",
  slug: "demo",
  displayName: "Demo Co",
  status: "active",
  plan: "standard",
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

/** The operator console on its own host, with the Gateway's operator BFF mocked. */
async function mockOperator(page: Page, calls: string[]) {
  let signedIn = false;
  let tenant = { ...demo };
  await page.route(/\/(api|bff)\//, async (route) => {
    const request = route.request();
    const key = `${request.method()} ${new URL(request.url()).pathname}`;
    calls.push(key);
    if (key === "POST /bff/operator/login") {
      signedIn = request.headers()["x-csrf"] === "1";
      return route.fulfill({ status: signedIn ? 204 : 403 });
    }
    if (!signedIn) return route.fulfill({ status: 401 });
    if (key === "GET /api/tenancy/v1/operator/me") return json(route, { id: "o1", email: "ops@example.com", displayName: "Ops One" });
    if (key === "GET /api/tenancy/v1/operator/tenants") return json(route, [tenant]);
    if (key === `GET /api/tenancy/v1/operator/tenants/${demo.id}`) return json(route, tenant);
    if (key === "GET /api/tenancy/v1/operator/principals") return json(route, page_([gateway]));
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
