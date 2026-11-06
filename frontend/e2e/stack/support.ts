import { createHmac } from "node:crypto";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { expect, type Browser, type Page } from "@playwright/test";

const root = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "..");
export const port = process.env.GATEWAY_PORT ?? "7443";
export const origin = (slug: string) => `https://${slug}.localhost:${port}`;

/** A long passphrase nobody has used before (the sign-in service checks passwords against known breaches). */
export const password = "Lantern-Cobalt-Meadow-4719-quiet";

/** Mailpit, the mail catcher the stack sends every email to (its web address, as in the README). */
export const mailpit = process.env.MAILPIT_URL ?? "http://localhost:8025";

/**
 * The emails Mailpit holds for an address, newest first: subject and whether the text body links to the sign-in host's invitation page.
 * The links themselves are never returned, so a failing test can't print one.
 */
export async function emailsTo(address: string): Promise<{ subject: string; hasInvitationLink: boolean }[]> {
  const list = (await (await fetch(`${mailpit}/api/v1/messages?limit=200`)).json()) as {
    messages: { ID: string; Subject: string; To: { Address: string }[] }[];
  };
  const mine = list.messages.filter((m) => m.To.some((to) => to.Address.toLowerCase() === address.toLowerCase()));
  return Promise.all(
    mine.map(async (m) => {
      const detail = (await (await fetch(`${mailpit}/api/v1/message/${m.ID}`)).json()) as { Text: string };
      return { subject: m.Subject, hasInvitationLink: detail.Text.includes("/account/invitation?") };
    }),
  );
}

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

/** The development operator that scripts/dev-up creates. */
export function devOperator(): { email: string; password: string; totpSecret: string } {
  return JSON.parse(readFileSync(join(root, ".dev", "operator.json"), "utf8")) as { email: string; password: string; totpSecret: string };
}

/** The current TOTP code (RFC 6238: SHA-1, 30 s, 6 digits) for a base32 secret. */
export function totp(secret: string): string {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  let bits = "";
  for (const c of secret.replace(/=+$/, "").toUpperCase()) bits += alphabet.indexOf(c).toString(2).padStart(5, "0");
  const key = Buffer.from(bits.match(/.{8}/g)!.map((b) => Number.parseInt(b, 2)));
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 1000 / 30)));
  const hmac = createHmac("sha1", key).update(counter).digest();
  const offset = hmac[hmac.length - 1]! & 0x0f;
  return String((hmac.readUInt32BE(offset) & 0x7fffffff) % 1_000_000).padStart(6, "0");
}
