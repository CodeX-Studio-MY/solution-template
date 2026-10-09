import { defineConfig, devices } from "@playwright/test";

/**
 * The scenario against the REAL stack that scripts/dev-up starts (Gateway with the mkcert certificate, every service, both demo
 * tenants). Run `scripts/dev-up`, then `pnpm e2e:stack`. The mocked tests in ./tests need no stack.
 */
export default defineConfig({
  testDir: "./stack",
  fullyParallel: false,
  workers: 1,
  timeout: 90_000,
  reporter: "list",
  use: { ignoreHTTPSErrors: true, trace: "retain-on-failure" },
  projects: [{ name: "stack", use: { ...devices["Desktop Chrome"] } }],
});
