import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { setTenantForTest, setupI18nForTest } from "@acme/web-core/testing";
import en from "../locales/en.json";
import BudgetOverview from "./BudgetOverview";

const budget = (code: string, amount: number, used: number) => ({
  code,
  name: `${code} name`,
  currency: "MYR",
  amount,
  used,
  remaining: amount - used,
  eTag: '"b"',
});

function respond(body: unknown) {
  vi.mocked(fetch).mockResolvedValue(new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } }));
}

function renderCard() {
  return render(
    <MemoryRouter>
      <BudgetOverview />
    </MemoryRouter>,
  );
}

describe("BudgetOverview", () => {
  beforeAll(async () => {
    await setupI18nForTest({ "cost-center-budget": { en }, common: { en: { widgets: { viewAll: "View all" } } } });
    setTenantForTest();
  });
  beforeEach(() => vi.stubGlobal("fetch", vi.fn()));
  afterEach(() => vi.unstubAllGlobals());

  it("lists the budgets closest to their limit first, with how much each has used", async () => {
    respond([budget("CC-1", 1000, 100), budget("CC-2", 1000, 950), budget("CC-3", 1000, 500)]);
    renderCard();

    expect(await screen.findByRole("heading", { name: "Budgets" })).toBeInTheDocument();
    const codes = (await screen.findAllByText(/^CC-\d /)).map((node) => node.textContent?.slice(0, 4));
    expect(codes).toEqual(["CC-2", "CC-3", "CC-1"]);
    expect(screen.getByText("95%")).toBeInTheDocument();
  });

  it("flags a budget that is used up", async () => {
    respond([budget("CC-9", 1000, 1200)]);
    renderCard();
    expect(await screen.findByText("120%")).toBeInTheDocument();
  });

  it("says so when there are no budgets", async () => {
    respond([]);
    renderCard();
    expect(await screen.findByText("No budgets yet.")).toBeInTheDocument();
  });
});
