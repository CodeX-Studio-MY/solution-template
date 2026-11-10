import { expect, test, type Route } from "@playwright/test";
import { expectAccessible, mockApp, watchProblems } from "./support";

const json = (route: Route, body: unknown, status = 200) =>
  route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) });

test("anyone signed in sends feedback from the header: type, rating, message and the page they were on", async ({ page }) => {
  const problems = watchProblems(page);
  let sent: unknown;
  await mockApp(page, {
    routes: {
      "POST /api/tenancy/v1/feedback": async (route) => {
        sent = route.request().postDataJSON();
        await json(route, { feedbackId: "88888888-8888-8888-8888-888888888881" }, 201);
      },
    },
  });
  await page.goto("/settings");
  await page.getByRole("button", { name: "Send feedback" }).click();
  const dialog = page.getByRole("dialog", { name: "Send feedback" });
  await dialog.getByRole("button", { name: "Bug" }).click();
  await dialog.locator("label", { hasText: "4 Stars" }).click();
  await dialog.getByLabel("Your message").fill("The export button does nothing");
  await expectAccessible(page);
  await dialog.getByRole("button", { name: "Send", exact: true }).click();

  await expect(page.getByText("Feedback sent. Thank you.")).toBeVisible();
  await expect(dialog).toHaveCount(0);
  expect(sent).toEqual({ kind: "bug", rating: 4, message: "The export button does nothing", pagePath: "/settings" });
  expect(problems).toEqual([]);
});

test("a message is required: the server's answer is shown next to the field and the dialog stays open", async ({ page }) => {
  await mockApp(page, {
    routes: {
      "POST /api/tenancy/v1/feedback": (route) =>
        json(
          route,
          { code: "platform.validation", status: 400, errors: { message: ["message is required."] }, title: "Validation failed" },
          400,
        ),
    },
  });
  await page.goto("/");
  await page.getByRole("button", { name: "Send feedback" }).click();
  const dialog = page.getByRole("dialog", { name: "Send feedback" });
  await dialog.getByRole("button", { name: "Send", exact: true }).click();
  await expect(dialog).toBeVisible();
});

test("a feedback admin reads what the organisation's people sent, in Settings", async ({ page }) => {
  const problems = watchProblems(page);
  await mockApp(page, {
    roles: ["feedback:admin"],
    routes: {
      "GET /api/tenancy/v1/feedback": (route) =>
        json(route, {
          items: [
            {
              feedbackId: "88888888-8888-8888-8888-888888888881",
              tenantId: "44444444-4444-4444-4444-444444444444",
              tenantSlug: "demo",
              tenantName: "Demo Co",
              senderId: "99999999-9999-9999-9999-999999999999",
              senderName: "Aisha Rahman",
              kind: "idea",
              rating: 5,
              message: "Please add a dark mode",
              pagePath: "/base",
              status: "reviewed",
              createdOn: "2026-10-30T09:30:00Z",
              eTag: '"f1"',
            },
          ],
          pageNumber: 1,
          pageSize: 25,
          totalCount: 1,
          totalPages: 1,
          hasNext: false,
        }),
    },
  });
  await page.goto("/");
  await page.getByRole("link", { name: "Platform settings" }).click();
  await page.getByRole("navigation", { name: "Settings sections" }).getByRole("link", { name: "Feedback" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Feedback" })).toBeVisible();
  await expect(
    page
      .getByRole("table", { name: "Feedback" })
      .getByRole("row", { name: /Aisha Rahman.*Idea.*Please add a dark mode.*\/base.*Reviewed/ }),
  ).toBeVisible();
  await expectAccessible(page);
  expect(problems).toEqual([]);
});

test("without the Feedback admin role the list is not offered", async ({ page }) => {
  await mockApp(page, { roles: ["tenant-settings:admin"] });
  await page.goto("/");
  await page.getByRole("link", { name: "Platform settings" }).click();
  await expect(page.getByRole("navigation", { name: "Settings sections" }).getByRole("link", { name: "Feedback" })).toHaveCount(0);
});
