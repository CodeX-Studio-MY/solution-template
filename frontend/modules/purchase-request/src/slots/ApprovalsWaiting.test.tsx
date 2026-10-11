import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { setTenantForTest, setupI18nForTest, signInForTest } from "@acme/web-core/testing";
import en from "../locales/en.json";
import ApprovalsWaiting from "./ApprovalsWaiting";

const row = (id: string, number: string, title: string) => ({
  id,
  number,
  title,
  status: "submitted",
  businessDate: "2026-10-01",
  total: 120.5,
  currency: "MYR",
});

function respond(items: unknown[]) {
  const body = { items, pageNumber: 1, pageSize: 5, totalCount: items.length, hasNext: false };
  vi.mocked(fetch).mockResolvedValue(new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } }));
}

function renderCard() {
  return render(
    <MemoryRouter>
      <ApprovalsWaiting />
    </MemoryRouter>,
  );
}

describe("ApprovalsWaiting", () => {
  beforeAll(async () => {
    await setupI18nForTest({ "purchase-request": { en } });
    setTenantForTest();
  });
  beforeEach(() => vi.stubGlobal("fetch", vi.fn()));
  afterEach(() => vi.unstubAllGlobals());

  it("shows a manager the requests waiting for a decision, each linking to its page", async () => {
    signInForTest(["purchase-request:manager"]);
    respond([row("r-1", "PR-0001", "Laptops"), row("r-2", "PR-0002", "Chairs")]);
    renderCard();

    expect(await screen.findByRole("heading", { name: "Waiting for your approval" })).toBeInTheDocument();
    expect(await screen.findByRole("link", { name: "PR-0001" })).toHaveAttribute("href", "/purchase-request/r-1");
    expect(screen.getByText("Chairs")).toBeInTheDocument();
  });

  it("says so when nothing is waiting", async () => {
    signInForTest(["purchase-request:manager"]);
    respond([]);
    renderCard();
    expect(await screen.findByText("No requests are waiting for approval.")).toBeInTheDocument();
  });

  it("shows nothing to someone who can't decide, and asks nobody", () => {
    signInForTest(["purchase-request:user"]);
    const { container } = renderCard();
    expect(container).toBeEmptyDOMElement();
    expect(fetch).not.toHaveBeenCalled();
  });
});
