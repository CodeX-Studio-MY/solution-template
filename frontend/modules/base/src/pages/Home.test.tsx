import { render, screen } from "@testing-library/react";
import { beforeAll, describe, expect, it } from "vitest";
import { setTenantForTest, setupI18nForTest, TestProviders } from "@saaskit/web-core/testing";
import { moduleKey } from "../basePath";
import en from "../locales/en.json";
import manifest from "../manifest";
import { Component as Home } from "./Home";

describe("Home", () => {
  beforeAll(async () => {
    await setupI18nForTest({ [moduleKey]: { en } });
    setTenantForTest();
  });

  it("says there is no master data yet", () => {
    render(
      <TestProviders>
        <Home />
      </TestProviders>,
    );
    expect(screen.getByRole("heading", { level: 1, name: "Base" })).toBeVisible();
    expect(screen.getByRole("heading", { name: "No master data yet" })).toBeVisible();
  });

  it("has one menu entry, to this page", () => {
    expect(manifest.menu).toHaveLength(1);
    expect(manifest.menu[0]?.path).toBe("");
    expect(manifest.routes).toHaveLength(1);
  });
});
