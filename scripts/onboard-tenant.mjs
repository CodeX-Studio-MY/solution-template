// Creates a fully isolated tenant in the stack that scripts/dev-up started: its own database (created and migrated), its modules
// for the plan, and a first-admin invitation. It signs in as the development operator (credentials saved by dev-up in .dev/operator.json).
//
//   scripts/onboard-tenant <slug> [--plan standard|plus] [--name "Display Name"] [--admin-email you@example.com] [--url http://localhost:5002]
//
// Local development only: in production, operators use the operator console, which is out of scope for this template.
import { createHmac } from "node:crypto";
import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const args = process.argv.slice(2);
const slug = args.find((a) => !a.startsWith("--"));
const option = (name) => (args.includes(name) ? args[args.indexOf(name) + 1] : undefined);
if (!slug) {
  console.error('Usage: scripts/onboard-tenant <slug> [--plan standard|plus] [--name "Display Name"] [--admin-email you@example.com] [--url http://localhost:5002]');
  process.exit(2);
}

const operatorFile = join(root, ".dev", "operator.json");
if (!existsSync(operatorFile)) {
  console.error("No development operator found. Start the stack with scripts/dev-up first (it creates one).");
  process.exit(1);
}
const operator = JSON.parse(readFileSync(operatorFile, "utf8"));

/** RFC 6238: the 6-digit code for the current 30-second step, from a base32 secret. */
function totp(secret) {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  let bits = "";
  for (const c of secret.replace(/=+$/, "").toUpperCase()) bits += alphabet.indexOf(c).toString(2).padStart(5, "0");
  const key = Buffer.from(bits.match(/.{8}/g).map((b) => Number.parseInt(b, 2)));
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 1000 / 30)));
  const hmac = createHmac("sha1", key).update(counter).digest();
  const offset = hmac[hmac.length - 1] & 0x0f;
  return String((hmac.readUInt32BE(offset) & 0x7fffffff) % 1_000_000).padStart(6, "0");
}

async function findTenancy() {
  const candidates = option("--url") ? [option("--url")] : ["http://localhost:5002", "http://localhost:5001"];   // 5001 when everything runs in one process
  for (const url of candidates) {
    try {
      if ((await fetch(`${url}/health/ready`)).ok) return url;
    } catch {
      // try the next
    }
  }
  console.error("The Tenancy service isn't reachable. Is the stack running (scripts/dev-up)?");
  process.exit(1);
}

const base = `${await findTenancy()}/api/tenancy/v1/operator`;
const headers = { "Content-Type": "application/json", "X-CSRF": "1" };
const session = await fetch(`${base}/sessions`, { method: "POST", headers, body: JSON.stringify({ email: operator.email, password: operator.password, totpCode: totp(operator.totpSecret) }) });
if (!session.ok) {
  console.error(`Operator sign-in failed (${session.status}): ${await session.text()}`);
  process.exit(1);
}
const { token } = await session.json();

const plan = option("--plan") ?? "standard";
const displayName = option("--name") ?? slug.replace(/(^|-)([a-z])/g, (_, s, c) => (s ? " " : "") + c.toUpperCase());
const adminEmail = option("--admin-email") ?? `admin@${slug}.example`;
const response = await fetch(`${base}/tenants`, {
  method: "POST",
  headers: { ...headers, Authorization: `Operator ${token}` },
  body: JSON.stringify({ slug, displayName, plan, adminEmail, adminName: `${displayName} Admin` }),
});
if (!response.ok) {
  console.error(`Onboarding failed (${response.status}): ${await response.text()}`);
  process.exit(1);
}
const result = await response.json();

const invitationsFile = join(root, ".dev", "invitations.json");
const invitations = existsSync(invitationsFile) ? JSON.parse(readFileSync(invitationsFile, "utf8")) : {};
if (result.adminInvitationUrl) invitations[slug] = result.adminInvitationUrl;
writeFileSync(invitationsFile, JSON.stringify(invitations, null, 2));

console.log(`Tenant '${slug}' is ready (${plan} plan).`);
console.log(`  App:         https://${slug}.localhost:7443/`);
console.log(result.adminInvitationUrl ? `  Invitation:  ${result.adminInvitationUrl}` : "  The tenant already existed; its invitation link was printed when it was created.");
