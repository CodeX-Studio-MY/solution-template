import { render, screen } from "@testing-library/react";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { setTenantForTest, setupI18nForTest } from "@farm360/web-core/testing";
import { moduleKey } from "../basePath";
import en from "../locales/en.json";
import { Component as FarmList } from "./FarmList";

const page = (items: unknown[]) => ({ items, pageNumber: 1, pageSize: 50, totalCount: items.length, totalPages: 1, hasNext: false });

const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } });

describe("FarmList", () => {
  beforeAll(async () => {
    await setupI18nForTest({ [moduleKey]: { en } });
    setTenantForTest();
  });
  beforeEach(() => vi.stubGlobal("fetch", vi.fn()));
  afterEach(() => vi.unstubAllGlobals());

  it("lists the farms with their code and type", async () => {
    vi.mocked(fetch).mockResolvedValue(
      json(page([{ id: "1", name: "Ladang Hijau", code: "FRM-001", farmType: "greenhouse", createdOn: "2026-10-20T01:00:00Z", eTag: '"v1"' }])),
    );
    render(<FarmList />);
    expect(await screen.findByRole("cell", { name: "Ladang Hijau" })).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "FRM-001" })).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "Greenhouse" })).toBeInTheDocument();
  });

  it("explains an empty list", async () => {
    vi.mocked(fetch).mockResolvedValue(json(page([])));
    render(<FarmList />);
    expect(await screen.findByText("No farms yet")).toBeInTheDocument();
  });
});
