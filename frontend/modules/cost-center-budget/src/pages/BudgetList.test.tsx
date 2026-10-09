import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { setTenantForTest, setupI18nForTest } from "@acme/web-core/testing";
import en from "../locales/en.json";
import { Component as BudgetList } from "./BudgetList";

const budget = { code: "CC-100", name: "Facilities", currency: "MYR", amount: 10000, used: 1500, remaining: 8500, eTag: '"b1"' };

describe("BudgetList", () => {
  beforeAll(async () => {
    await setupI18nForTest({ "cost-center-budget": { en } });
    setTenantForTest();
  });
  beforeEach(() => vi.stubGlobal("fetch", vi.fn()));
  afterEach(() => vi.unstubAllGlobals());

  it("lists the budgets and changes one with the version it saw", async () => {
    const calls: Request[] = [];
    vi.mocked(fetch).mockImplementation((input) => {
      const request = input as Request;
      calls.push(request);
      const body = request.method === "GET" ? [budget] : { ...budget, amount: 12000 };
      return Promise.resolve(new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } }));
    });
    render(<BudgetList />);
    expect(await screen.findByRole("cell", { name: "Facilities" })).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Change" }));
    const amount = screen.getByLabelText(/Budget/);
    await userEvent.clear(amount);
    await userEvent.type(amount, "12000");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(calls.some((c) => c.method === "PUT")).toBe(true));
    const put = calls.find((c) => c.method === "PUT")!;
    expect(new URL(put.url).pathname).toBe("/api/cost-center-budget/v1/budgets/CC-100");
    expect(put.headers.get("If-Match")).toBe('"b1"');
    expect(await put.clone().json()).toEqual({ name: "Facilities", amount: 12000 });
  });

  it("tells finance there are no budgets yet", async () => {
    vi.mocked(fetch).mockResolvedValue(new Response("[]", { status: 200, headers: { "Content-Type": "application/json" } }));
    render(<BudgetList />);
    expect(await screen.findByText("No budgets yet")).toBeInTheDocument();
  });
});
