import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { ESLint } from "eslint";
import { describe, expect, it } from "vitest";

/**
 * The frontend's "make the wrong thing impossible" rules must actually fail the build. Each case lints a snippet as if it lived
 * in a given file and expects the rule (or no rule) to fire.
 */
const root = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const eslint = new ESLint({ cwd: root, overrideConfigFile: join(root, "eslint.config.js") });

async function messages(code: string, file: string) {
  const [result] = await eslint.lintText(code, { filePath: join(root, file) });
  return result!.messages.map((m) => `${m.ruleId}: ${m.message}`);
}

const moduleFile = "modules/purchase-request/src/pages/Example.tsx";

describe("frontend lint rules", () => {
  it("rejects colour literals everywhere except tokens.ts", async () => {
    expect(await messages('export const c = "#ff0000";', moduleFile)).toEqual([expect.stringContaining("tokens.ts")]);
    expect(await messages('export const c = "rgb(1, 2, 3)";', "apps/shell/src/x.ts")).toEqual([expect.stringContaining("tokens.ts")]);
    expect(await messages('export const c = "#ff0000";', "packages/ui-kit/src/theme/tokens.ts")).toEqual([]);
  });

  it("rejects /api/ literals, except as the path of a typed client call", async () => {
    expect(await messages('export const u = "/api/purchase-request/v1/requests";', moduleFile)).toEqual([
      expect.stringContaining("apiPath"),
    ]);
    expect(await messages("export const u = `/api/x/${1}`;", moduleFile)).toEqual([expect.stringContaining("apiPath")]);
    expect(await messages('declare const c: { GET(p: string): void }; c.GET("/api/purchase-request/v1/requests");', moduleFile)).toEqual(
      [],
    );
  });

  it("rejects host names in code", async () => {
    expect(await messages('export const u = "http://localhost:5001";', moduleFile)).toEqual([expect.stringContaining("No host names")]);
  });

  it("rejects hand-written API types", async () => {
    expect(await messages("export interface RequestDto { id: string }", moduleFile)).toEqual([expect.stringContaining("generated")]);
  });

  it("rejects branching on tenant identity", async () => {
    expect(await messages('declare const tenant: { slug: string }; export const a = tenant.slug === "demo-plus";', moduleFile)).toEqual([
      expect.stringContaining("tenant identity"),
    ]);
  });

  it("rejects `any`", async () => {
    expect(await messages("export const a: any = 1;", moduleFile)).toEqual([expect.stringContaining("no-explicit-any")]);
  });

  it("keeps the layers apart: the shell never imports a module, a module never imports the shell, web-core never imports ui-kit", async () => {
    expect(await messages('import "../../../modules/purchase-request/src/manifest";', "apps/shell/src/x.ts")).toEqual([
      expect.stringContaining("boundaries/dependencies"),
    ]);
    expect(await messages('import "../../../../apps/shell/src/App";', moduleFile)).toEqual([
      expect.stringContaining("boundaries/dependencies"),
    ]);
    expect(await messages('import "../../ui-kit/src/index";', "packages/web-core/src/x.ts")).toEqual([
      expect.stringContaining("boundaries/dependencies"),
    ]);
  });

  it("allows what is meant to be allowed", async () => {
    expect(await messages('import { useFormatters } from "@acme/web-core";\nexport const a = useFormatters;', moduleFile)).toEqual([]);
    expect(await messages('import { PageHeader } from "@acme/ui-kit";\nexport const a = PageHeader;', moduleFile)).toEqual([]);
  });
});
