import { test } from "@playwright/test";
import { acceptInvitation, signIn } from "./support";

/** Against the real stack from scripts/dev-up: the demo tenant's first admin accepts the invitation and signs in. */
test("the demo tenant's admin signs in and reaches the home page", async ({ browser, page }) => {
  await acceptInvitation(browser, "demo");
  await signIn(page, "demo");
});
