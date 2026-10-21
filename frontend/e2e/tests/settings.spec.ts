import { expect, test, type Route } from "@playwright/test";
import { expectAccessible, mockApp, watchProblems } from "./support";

const json = (route: Route, body: unknown, status = 200) =>
  route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) });

const admin = ["access-management:admin", "tenant-settings:admin"];
const siti = {
  id: "11111111-1111-1111-1111-111111111111",
  type: "user",
  displayName: "Siti",
  email: "siti@example.com",
  isActive: true,
  status: "active",
  clientId: null,
  lastSeenOn: null,
  eTag: '"v1"',
};

test("an admin opens Settings from the gear, invites a user, and sees it confirmed", async ({ page }) => {
  const problems = watchProblems(page);
  let invited: unknown;
  await mockApp(page, {
    roles: admin,
    routes: {
      "GET /api/identity/v1/principals": (route) => json(route, [siti]),
      "POST /api/identity/v1/users/invitations": async (route) => {
        invited = route.request().postDataJSON();
        await json(route, { principalId: "22222222-2222-2222-2222-222222222222", expiresOn: "2026-10-30T00:00:00Z" }, 201);
      },
    },
  });
  await page.goto("/");
  await page.getByRole("link", { name: "Platform settings" }).click();
  await page.getByRole("navigation", { name: "Settings sections" }).getByRole("link", { name: "Users" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Users" })).toBeVisible();
  await expect(page.getByRole("cell", { name: "siti@example.com" })).toBeVisible();
  await expectAccessible(page);

  await page.getByRole("button", { name: "Invite user" }).click();
  const dialog = page.getByRole("dialog", { name: "Invite a user" });
  await dialog.getByLabel("Email").fill("lee@example.com");
  await dialog.getByLabel("Name").fill("Lee");
  await dialog.getByRole("combobox", { name: "Billing" }).click();
  await page.getByRole("option", { name: "Manager" }).click();
  await dialog.getByRole("button", { name: "Invite user" }).click();

  await expect(page.getByText("Invitation sent to lee@example.com.")).toBeVisible();
  expect(invited).toEqual({ email: "lee@example.com", displayName: "Lee", roles: [{ module: "billing", role: "manager" }] });
  expect(problems).toEqual([]);
});

test("deleting a group asks first, and Cancel deletes nothing", async ({ page }) => {
  const deletes: string[] = [];
  const group = { id: "33333333-3333-3333-3333-333333333333", name: "Field team", isActive: true, memberCount: 0, eTag: '"g1"' };
  await mockApp(page, {
    roles: admin,
    routes: {
      "GET /api/identity/v1/groups": (route) => json(route, [group]),
      [`GET /api/identity/v1/groups/${group.id}/members`]: (route) => json(route, []),
      [`GET /api/identity/v1/groups/${group.id}/assignments`]: (route) => json(route, []),
      "GET /api/identity/v1/principals": (route) => json(route, [siti]),
      [`DELETE /api/identity/v1/groups/${group.id}`]: async (route) => {
        deletes.push(route.request().url());
        await route.fulfill({ status: 204 });
      },
    },
  });
  await page.goto("/settings/groups");
  await page.getByRole("button", { name: "Field team" }).click();
  await page.getByRole("button", { name: "Delete" }).click();

  const confirm = page.getByRole("dialog", { name: "Delete Field team?" });
  await expect(confirm.getByRole("button", { name: "Cancel" })).toBeFocused();
  await confirm.getByRole("button", { name: "Cancel" }).click();
  expect(deletes).toEqual([]);

  await page.getByRole("button", { name: "Delete" }).click();
  await page.getByRole("dialog", { name: "Delete Field team?" }).getByRole("button", { name: "Delete" }).click();
  await expect(page.getByText("Group deleted.")).toBeVisible();
  expect(deletes).toHaveLength(1);
});

test("branding saves only the changed colour", async ({ page }) => {
  let saved: unknown;
  const view = {
    moduleKey: "tenant-settings",
    values: { primaryColor: "", logoUrl: "" },
    overrides: {},
    settings: [
      { name: "primaryColor", tenantEditable: true, default: "", allowedValues: null },
      { name: "logoUrl", tenantEditable: true, default: "", allowedValues: null },
    ],
  };
  await mockApp(page, {
    roles: admin,
    routes: {
      "GET /api/tenancy/v1/settings/tenant-settings": (route) => json(route, view),
      "PUT /api/tenancy/v1/settings/tenant-settings": async (route) => {
        saved = route.request().postDataJSON();
        await json(route, { ...view, values: { ...view.values, ...(saved as object) }, overrides: saved });
      },
    },
  });
  await page.goto("/settings/branding");
  await page.getByLabel("Primary colour").fill("#aa3300");
  await page.getByRole("button", { name: "Save" }).click();
  await expect(page.getByText("Branding saved.")).toBeVisible();
  expect(saved).toEqual({ primaryColor: "#aa3300" });
});

test("someone without a role in Settings has no gear and is turned away", async ({ page }) => {
  await mockApp(page, { roles: ["billing:user"] });
  await page.goto("/settings/users");
  await expect(page.getByText("You don't have access to this part of Settings.")).toBeVisible();
  await expect(page.getByRole("link", { name: "Platform settings" })).toHaveCount(0);
});
