import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { expect, type Browser, type Page } from "@playwright/test";

const root = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "..");
export const port = process.env.GATEWAY_PORT ?? "7443";
export const origin = (slug: string) => `https://${slug}.localhost:${port}`;

/** A long passphrase nobody has used before (the sign-in service checks passwords against known breaches). */
export const password = "Lantern-Cobalt-Meadow-4719-quiet";

export function invitation(slug: string): string | undefined {
  try {
    return (JSON.parse(readFileSync(join(root, ".dev", "invitations.json"), "utf8")) as Record<string, string>)[slug];
  } catch {
    return undefined;
  }
}

/** The first admin accepts the invitation printed by scripts/dev-up, once. A second run finds it used and goes on to sign in. */
export async function acceptInvitation(browser: Browser, slug: string): Promise<void> {
  const link = invitation(slug);
  if (!link) return;
  const page = await browser.newPage({ ignoreHTTPSErrors: true });
  await page.goto(link);
  if (await page.getByLabel("New password").isVisible()) {
    await page.getByLabel("New password").fill(password);
    await page.getByRole("button", { name: "Save password" }).click();
  }
  await page.close();
}

export async function signIn(page: Page, slug: string): Promise<void> {
  await page.goto(`${origin(slug)}/`);
  await page.getByLabel("Email").fill(`admin@${slug}.example`);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Welcome" })).toBeVisible({ timeout: 30_000 });
}
