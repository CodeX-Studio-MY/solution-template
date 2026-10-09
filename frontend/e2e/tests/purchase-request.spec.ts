import { expect, test, type Route } from "@playwright/test";
import { expectAccessible, mockApp, sampleRequest, watchProblems } from "./support";

test("buttons follow the actions the server allows, never the role", async ({ page }) => {
  const problems = watchProblems(page);
  await mockApp(page, { roles: ["purchase-request:manager"] });
  await page.goto(`/purchase-request/${sampleRequest.id}`);

  await expect(page.getByRole("heading", { name: `${sampleRequest.number} · ${sampleRequest.title}` })).toBeVisible();
  await expect(page.getByRole("button", { name: "Approve" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Reject" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Edit" })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Submit for approval" })).toHaveCount(0);

  await expectAccessible(page);
  expect(problems).toEqual([]);
});

test("approving sends the CSRF header, an Idempotency-Key and the version it saw", async ({ page }) => {
  let seen: Record<string, string> = {};
  await mockApp(page, {
    roles: ["purchase-request:manager"],
    routes: {
      [`POST /api/purchase-request/v1/requests/${sampleRequest.id}/approve`]: (route) => {
        seen = route.request().headers();
        return route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({ ...sampleRequest, status: "approved", allowedActions: [] }),
        });
      },
    },
  });
  await page.goto(`/purchase-request/${sampleRequest.id}`);
  await page.getByRole("button", { name: "Approve" }).click();

  await expect.poll(() => seen["x-csrf"]).toBe("1");
  expect(seen["idempotency-key"]).toMatch(/^[0-9a-f-]{36}$/);
  expect(seen["if-match"]).toBe('"v1"');
});

test("the server's validation messages appear next to the fields", async ({ page }) => {
  await mockApp(page, {
    routes: {
      "POST /api/purchase-request/v1/requests": (route) =>
        route.fulfill({
          status: 400,
          contentType: "application/problem+json",
          body: JSON.stringify({
            status: 400,
            code: "platform.validation",
            message: "Please correct the highlighted fields.",
            errors: [
              { field: "title", code: "validation.required", message: "Title is required." },
              { field: "lines[0].item", code: "validation.required", message: "Item is required." },
            ],
          }),
        }),
    },
  });
  await page.goto("/purchase-request/new");
  await page.getByRole("button", { name: "Save" }).click();
  await expect(page.getByText("Title is required.")).toBeVisible();
  await expect(page.getByText("Item is required.")).toBeVisible();
  await expectAccessible(page);
});

test("a tenant's custom fields come from the definitions, with no tenant-specific code", async ({ page }) => {
  await mockApp(page, {
    routes: {
      "GET /api/purchase-request/v1/custom-fields": (route) =>
        route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify([
            { key: "cost-center", label: "Cost center", type: "list", required: true, options: ["CC-100", "CC-200"], displayOrder: 1 },
          ]),
        }),
    },
  });
  await page.goto("/purchase-request/new");
  await expect(page.getByRole("heading", { name: "Additional information" })).toBeVisible();
  await expect(page.getByLabel("Cost center")).toBeVisible();
  await expectAccessible(page);
});

test("draft sharing appears only when the feature is on", async ({ page }) => {
  const draft = { ...sampleRequest, status: "draft", allowedActions: ["edit", "delete", "submit", "uploadQuote"] };
  const routes = {
    [`GET /api/purchase-request/v1/requests/${sampleRequest.id}`]: (route: Route) =>
      route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(draft) }),
  };

  await mockApp(page, { features: { "purchase-request.draft-sharing": false }, routes });
  await page.goto(`/purchase-request/${sampleRequest.id}`);
  await expect(page.getByRole("button", { name: "Submit for approval" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Copy link to draft" })).toHaveCount(0);

  const other = await page.context().newPage();
  await mockApp(other, { features: { "purchase-request.draft-sharing": true }, routes });
  await other.goto(`/purchase-request/${sampleRequest.id}`);
  await expect(other.getByRole("button", { name: "Copy link to draft" })).toBeVisible();
});
