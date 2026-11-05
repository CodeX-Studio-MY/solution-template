import { expect, test } from "@playwright/test";
import { acceptInvitation, signIn } from "./support";

/** The Base module that ships with the template, and access for applications, against the real services. */
test("the demo admin finds Base in the menu and opens its empty page", async ({ browser, page }) => {
  await acceptInvitation(browser, "demo");
  await signIn(page, "demo");

  await page.getByRole("navigation", { name: "Main navigation" }).getByRole("link", { name: "Base" }).click();
  await expect(page).toHaveURL(/\/base$/);
  await expect(page.getByRole("heading", { level: 1, name: "Base" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "No master data yet" })).toBeVisible();
});

test("an application is added to a group and gets the group's access, shown with its type", async ({ browser, page }) => {
  await acceptInvitation(browser, "demo");
  await signIn(page, "demo");
  const run = Date.now().toString(36);

  await page.getByRole("link", { name: "Platform settings" }).click();
  const nav = page.getByRole("navigation", { name: "Settings sections" });

  await nav.getByRole("link", { name: "Applications" }).click();
  await page.getByRole("button", { name: "Add application" }).click();
  await page.getByRole("dialog", { name: "Add an application" }).getByLabel("Name").fill(`Partner ${run}`);
  await page.getByRole("dialog", { name: "Add an application" }).getByRole("button", { name: "Create" }).click();
  await page
    .getByRole("dialog", { name: `Credentials for Partner ${run}` })
    .getByRole("button", { name: "Close" })
    .click();

  await nav.getByRole("link", { name: "Groups" }).click();
  await page.getByRole("button", { name: "New group" }).click();
  await page.getByRole("dialog", { name: "New group" }).getByLabel("Name").fill(`Integrations ${run}`);
  await page.getByRole("dialog", { name: "New group" }).getByRole("button", { name: "Save" }).click();
  await expect(page.getByText("Group created.")).toBeVisible();

  const panel = page.getByRole("dialog").filter({ has: page.getByRole("heading", { name: `Integrations ${run}` }) });
  await panel.getByLabel("Add a member").click();
  await page.getByRole("option", { name: `Partner ${run} (Application)` }).click();
  await panel.getByRole("button", { name: "Add" }).click();
  await expect(page.getByText("Member added.")).toBeVisible();
  await expect(panel.getByRole("listitem").filter({ hasText: `Partner ${run}` })).toContainText("Application");

  // The group gives the application a role in Base, and the application is removed again.
  await panel.getByRole("combobox", { name: "Base" }).click();
  await page.getByRole("option", { name: "User", exact: true }).click();
  await expect(page.getByText("Access updated.")).toBeVisible();
  await panel.getByRole("button", { name: `Remove Partner ${run} from the group` }).click();
  await expect(page.getByText("Member removed.")).toBeVisible();
});
