import { expect, test } from "@playwright/test";
import { acceptInvitation, signIn } from "./support";

/** Settings against the real services: the first admin of `demo` manages people, groups and branding. */
test("the demo admin invites a user, makes a group with a member, and changes the brand colour", async ({ browser, page }) => {
  await acceptInvitation(browser, "demo");
  await signIn(page, "demo");
  const run = Date.now().toString(36);

  await page.getByRole("link", { name: "Settings" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Profile" })).toBeVisible();
  await expect(page.getByLabel("Organisation name")).not.toHaveValue("");

  const nav = page.getByRole("navigation", { name: "Settings sections" });
  await nav.getByRole("link", { name: "Users" }).click();
  await page.getByRole("button", { name: "Invite user" }).click();
  const invite = page.getByRole("dialog", { name: "Invite a user" });
  await invite.getByLabel("Email").fill(`user-${run}@demo.example`);
  await invite.getByLabel("Name").fill(`User ${run}`);
  await invite.getByRole("button", { name: "Invite user" }).click();
  await expect(page.getByText(`Invitation sent to user-${run}@demo.example.`)).toBeVisible();
  await expect(page.getByRole("row", { name: new RegExp(`User ${run}.*Invited`) })).toBeVisible();

  await nav.getByRole("link", { name: "Groups" }).click();
  await page.getByRole("button", { name: "New group" }).click();
  await page.getByRole("dialog", { name: "New group" }).getByLabel("Name").fill(`Team ${run}`);
  await page.getByRole("dialog", { name: "New group" }).getByRole("button", { name: "Save" }).click();
  await expect(page.getByText("Group created.")).toBeVisible();
  const panel = page.getByRole("dialog").filter({ has: page.getByRole("heading", { name: `Team ${run}` }) });
  await panel.getByLabel("Add a member").click();
  await page.getByRole("option", { name: `User ${run}` }).click();
  await panel.getByRole("button", { name: "Add" }).click();
  await expect(page.getByText("Member added.")).toBeVisible();
  await expect(panel.getByText(`user-${run}@demo.example`)).toBeVisible();
  await page.keyboard.press("Escape");

  // The security log has the invitation, naming who it was for.
  await nav.getByRole("link", { name: "Security log" }).click();
  await expect(
    page.getByRole("table", { name: "Security log" }).getByRole("row", { name: new RegExp(`Invitation sent.*User ${run}`) }),
  ).toBeVisible();

  // An integration gets a client ID and a secret that is shown once.
  await nav.getByRole("link", { name: "Devices & integrations" }).click();
  await page.getByRole("button", { name: "Add integration" }).click();
  await page.getByRole("dialog", { name: "Add an integration" }).getByLabel("Name").fill(`ERP ${run}`);
  await page.getByRole("dialog", { name: "Add an integration" }).getByRole("button", { name: "Create" }).click();
  const credentials = page.getByRole("dialog", { name: `Credentials for ERP ${run}` });
  await expect(credentials).toContainText("Client ID: demo-service-");
  await credentials.getByRole("button", { name: "Close" }).click();
  await expect(page.getByRole("row", { name: new RegExp(`ERP ${run}.*Integration`) })).toBeVisible();

  await nav.getByRole("link", { name: "Branding" }).click();
  // A colour of its own per run, so an earlier run's saved value never makes this a no-op.
  const [r, g, b] = [0, 0, 0].map(() => 40 + Math.floor(Math.random() * 120));
  const hex = `#${[r, g, b].map((v) => v!.toString(16).padStart(2, "0")).join("")}`;
  await page.getByLabel("Primary colour", { exact: true }).fill(hex);
  await page.getByRole("button", { name: "Save" }).click();
  await expect(page.getByText("Branding saved.")).toBeVisible();
  // The app's own theme follows at once: primary-coloured controls take the new colour.
  await expect(page.getByRole("button", { name: "Upload logo" })).toHaveCSS("color", `rgb(${r}, ${g}, ${b})`);
  await page.getByRole("button", { name: /Use the default: Primary colour/ }).click();
  await expect(page.getByText("Branding saved.").first()).toBeVisible();
});
