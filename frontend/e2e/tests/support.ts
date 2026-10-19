import AxeBuilder from "@axe-core/playwright";
import { expect, type Page, type Route } from "@playwright/test";

/** The API is mocked in the browser: these tests are about the shell, module loading and the rules the UI follows. */
export interface Scenario {
  roles?: string[];
  modules?: { id: string; entry: string }[];
  features?: Record<string, boolean>;
  branding?: { primaryColor?: string };
  signedIn?: boolean;
  /** Overrides by "METHOD /path". */
  routes?: Record<string, (route: Route) => Promise<void> | void>;
}

const json = (route: Route, body: unknown, status = 200) =>
  route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) });

export async function mockApp(page: Page, scenario: Scenario = {}) {
  const {
    roles = [],
    modules = [],
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
