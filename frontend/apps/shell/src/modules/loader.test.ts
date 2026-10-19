import { describe, expect, it, vi } from "vitest";
import type { ModuleManifest } from "@saaskit/web-core";
import { setupI18nForTest } from "@saaskit/web-core/testing";
import { loadModules } from "./loader";

vi.mock("@module-federation/enhanced/runtime", () => ({
  getInstance: () => ({ registerRemotes: vi.fn() }),
  createInstance: () => ({ registerRemotes: vi.fn() }),
}));

const manifest = (id: string, overrides: Partial<ModuleManifest> = {}): { default: ModuleManifest } => ({
  default: { id, basePath: `/${id}`, routes: [], menu: [], i18n: { namespace: id }, ...overrides },
});

describe("loadModules", () => {
  it("loads each module's manifest and registers its strings", async () => {
    await setupI18nForTest();
    const load = vi.fn((id: string) => Promise.resolve(manifest(id, { i18n: { namespace: id, resources: { en: { menu: "Menu" } } } })));
    const result = await loadModules(load, [{ id: "goods-receipt", entry: "/mfe/x" }]);
    expect(result.failures).toEqual([]);
    expect(result.modules.map((m) => m.manifest.id)).toEqual(["goods-receipt"]);
  });

  it("reports a module that fails to load and carries on with the rest", async () => {
    await setupI18nForTest();
    const load = (id: string) => (id === "broken" ? Promise.reject(new Error("404")) : Promise.resolve(manifest(id)));
    const result = await loadModules(load, [
      { id: "broken", entry: "/mfe/broken" },
      { id: "goods-receipt", entry: "/mfe/pr" },
    ]);
    expect(result.modules.map((m) => m.manifest.id)).toEqual(["goods-receipt"]);
    expect(result.failures.map((f) => f.id)).toEqual(["broken"]);
  });

  it("rejects a manifest whose id or base path doesn't match the module key", async () => {
    await setupI18nForTest();
    const wrongId = () => Promise.resolve(manifest("something-else"));
    const wrongPath = (id: string) => Promise.resolve(manifest(id, { basePath: "/other" }));
    expect((await loadModules(wrongId, [{ id: "goods-receipt", entry: "x" }])).failures).toHaveLength(1);
    expect((await loadModules(wrongPath, [{ id: "goods-receipt", entry: "x" }])).failures).toHaveLength(1);
  });
});
