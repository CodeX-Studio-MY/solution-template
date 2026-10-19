import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { setTenantForTest, setupI18nForTest } from "@saaskit/web-core/testing";
import { moduleKey } from "../basePath";
import en from "../locales/en.json";
import { Component as ItemList } from "./ItemList";

const page = (items: unknown[]) => ({ items, pageNumber: 1, pageSize: 50, totalCount: items.length, totalPages: 1, hasNext: false });

describe("ItemList", () => {
  beforeAll(async () => {
    await setupI18nForTest({ [moduleKey]: { en } });
    setTenantForTest();
  });
  beforeEach(() => vi.stubGlobal("fetch", vi.fn()));
  afterEach(() => vi.unstubAllGlobals());

  it("lists the items and adds one", async () => {
    const calls: Request[] = [];
    vi.mocked(fetch).mockImplementation((input) => {
      const request = input as Request;
      calls.push(request);
      const body =
        request.method === "GET"
          ? page([{ id: "1", name: "Widget", createdOn: "2026-10-09T01:00:00Z", eTag: '"v1"' }])
          : { id: "2", name: "Gadget", createdOn: "2026-10-09T01:00:00Z", eTag: '"v1"' };
      return Promise.resolve(
        new Response(JSON.stringify(body), {
          status: request.method === "POST" ? 201 : 200,
          headers: { "Content-Type": "application/json" },
        }),
      );
    });
    render(<ItemList />);
    expect(await screen.findByRole("cell", { name: "Widget" })).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText(/Name/), "Gadget");
    await userEvent.click(screen.getByRole("button", { name: "Add" }));

    await waitFor(() => expect(calls.some((c) => c.method === "POST")).toBe(true));
    expect(
      await calls
        .find((c) => c.method === "POST")!
        .clone()
        .json(),
    ).toEqual({ name: "Gadget" });
  });

  it("explains an empty list", async () => {
    vi.mocked(fetch).mockResolvedValue(
      new Response(JSON.stringify(page([])), { status: 200, headers: { "Content-Type": "application/json" } }),
    );
    render(<ItemList />);
    expect(await screen.findByText("Nothing here yet")).toBeInTheDocument();
  });
});
