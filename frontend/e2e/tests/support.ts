import AxeBuilder from "@axe-core/playwright";
import { expect, type Page, type Route } from "@playwright/test";

/** The API is mocked in the browser: these tests are about the shell, the loaded module and the rules the UI follows. */
export interface Scenario {
  roles?: string[];
  modules?: { id: string; entry: string }[];
  features?: Record<string, boolean>;
  branding?: { primaryColor?: string };
  signedIn?: boolean;
  /** Overrides by "METHOD /path". */
  routes?: Record<string, (route: Route) => Promise<void> | void>;
}

export const purchaseRequestEntry = "/mfe/purchase-request/0.0.0-dev/mf-manifest.json";

const json = (route: Route, body: unknown, status = 200) =>
  route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) });

export const sampleRequest = {
  id: "11111111-1111-1111-1111-111111111111",
  number: "PR-20261009-0001",
  requesterId: "22222222-2222-2222-2222-222222222222",
  title: "Standing desks",
  description: "For the new office",
  status: "submitted",
  businessDate: "2026-10-09",
  currency: "MYR",
  total: 2400,
  lines: [{ item: "Desk", quantity: 4, unitPrice: 600, lineTotal: 2400 }],
  approvals: [],
  quoteFileId: null,
  createdOn: "2026-10-09T01:00:00Z",
  submittedOn: "2026-10-09T02:00:00Z",
  decidedOn: null,
  autoApproved: false,
  rejectionReason: null,
  goodsReceivedOn: null,
  allowedActions: ["approve", "reject"],
  eTag: '"v1"',
};

export async function mockApp(page: Page, scenario: Scenario = {}) {
  const {
    roles = ["purchase-request:user"],
    modules = [{ id: "purchase-request", entry: purchaseRequestEntry }],
    features = {},
    branding = {},
    signedIn = true,
  } = scenario;
  const handlers: Record<string, (route: Route) => Promise<void> | void> = {
    "GET /bff/user": (route) =>
      signedIn
        ? json(route, { subject: "22222222-2222-2222-2222-222222222222", tenant: "demo", principalType: "user", roles })
        : route.fulfill({ status: 401 }),
    "GET /bff/config": (route) =>
      json(route, {
        tenant: { slug: "demo", name: "Demo Co", timeZone: "Asia/Kuala_Lumpur", currency: "MYR", locale: "en" },
        defaults: { timeZone: "Asia/Kuala_Lumpur", currency: "MYR", locales: ["en", "ms", "zh"] },
        modules: { baseUrl: "/mfe" },
        apiPrefix: "/api",
      }),
    "GET /api/tenancy/v1/me/modules": (route) => json(route, { modules }),
    "GET /api/tenancy/v1/me/settings": (route) => json(route, { branding, features, values: {} }),
    "GET /api/purchase-request/v1/requests": (route) =>
      json(route, {
        items: [
          {
            id: sampleRequest.id,
            number: sampleRequest.number,
            title: sampleRequest.title,
            status: "submitted",
            total: 2400,
            currency: "MYR",
            businessDate: "2026-10-09",
            requesterId: sampleRequest.requesterId,
          },
        ],
        pageNumber: 1,
        pageSize: 20,
        totalCount: 1,
        totalPages: 1,
        hasNext: false,
      }),
    [`GET /api/purchase-request/v1/requests/${sampleRequest.id}`]: (route) => json(route, sampleRequest),
    "GET /api/purchase-request/v1/custom-fields": (route) => json(route, [], 200),
    ...scenario.routes,
  };

  await page.route(/\/(api|bff)\//, async (route) => {
    const url = new URL(route.request().url());
    const handler = handlers[`${route.request().method()} ${url.pathname}`];
    if (handler) await handler(route);
    else await route.fulfill({ status: 404, contentType: "application/json", body: "{}" });
  });
}

/** Every console error and CSP violation the page produced. A clean run has none. */
export function watchProblems(page: Page): string[] {
  const problems: string[] = [];
  page.on("console", (message) => {
    if (message.type() === "error") problems.push(message.text());
  });
  page.on("pageerror", (error) => problems.push(error.message));
  return problems;
}

/** WCAG 2.2 AA automated checks (axe); manual review is still needed for the rest. */
export async function expectAccessible(page: Page) {
  const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"]).analyze();
  expect(results.violations.map((v) => `${v.id}: ${v.help} (${v.nodes.length})`)).toEqual([]);
}
