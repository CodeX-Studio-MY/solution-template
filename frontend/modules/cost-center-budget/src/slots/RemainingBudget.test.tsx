import { render, screen } from "@testing-library/react";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { setTenantForTest, setupI18nForTest } from "@acme/web-core/testing";
import en from "../locales/en.json";
import RemainingBudget from "./RemainingBudget";

const budget = { code: "CC-100", name: "Facilities", currency: "MYR", amount: 10000, used: 1500, remaining: 8500, eTag: '"b"' };
const total = { amount: 1000, currency: "MYR" };

function respond(status: number, body: unknown) {
  vi.mocked(fetch).mockResolvedValue(new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } }));
}

describe("RemainingBudget", () => {
  beforeAll(async () => {
    await setupI18nForTest({ "cost-center-budget": { en } });
    setTenantForTest();
  });
  beforeEach(() => vi.stubGlobal("fetch", vi.fn()));
  afterEach(() => vi.unstubAllGlobals());

  it("shows what is left in the request's cost center", async () => {
    respond(200, budget);
    render(<RemainingBudget requestId="1" costCenter="CC-100" total={total} />);
    expect(await screen.findByRole("heading", { name: "Budget of CC-100" })).toBeInTheDocument();
    expect(screen.getByText(/8,500\.00/)).toBeInTheDocument();
    expect(screen.queryByText("This request is more than what is left.")).not.toBeInTheDocument();
  });

  it("warns when the request is more than what is left", async () => {
    respond(200, budget);
    render(<RemainingBudget requestId="1" costCenter="CC-100" total={{ amount: 9000, currency: "MYR" }} />);
    expect(await screen.findByText("This request is more than what is left.")).toBeInTheDocument();
  });

  it("shows nothing for a request without a cost center, and asks nobody", () => {
    const { container } = render(<RemainingBudget requestId="1" total={total} />);
    expect(container).toBeEmptyDOMElement();
    expect(fetch).not.toHaveBeenCalled();
  });

  it("shows nothing, quietly, for a cost center that has no budget", async () => {
    respond(404, { status: 404, code: "cost-center-budget.not-found", message: "That cost center has no budget." });
    const { container } = render(<RemainingBudget requestId="1" costCenter="CC-999" total={total} />);
    await vi.waitFor(() => expect(fetch).toHaveBeenCalled());
    expect(container).toBeEmptyDOMElement();
  });
});
