import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { setTenantForTest, setupI18nForTest, signInForTest } from "@farm360/web-core/testing";
import { moduleKey } from "../basePath";
import en from "../locales/en.json";
import type { FarmDto } from "../types";
import { Component as FarmSettings } from "./FarmSettings";

const farm = (overrides: Partial<FarmDto>): FarmDto => ({
  id: "f1",
  name: "Ladang Hijau",
  code: "FRM-001",
  farmType: "greenhouse",
  globalGapNumber: null,
  description: null,
  address: null,
  contactPerson: null,
  contactPhone: null,
  areaHectares: 12.5,
  latitude: null,
  longitude: null,
  orderSequence: 1,
  isExternal: false,
  isActive: true,
  createdOn: "2026-10-21T01:00:00Z",
  eTag: '"v1"',
  ...overrides,
});

const json = (body: unknown, status = 200) =>
  new Response(body === undefined ? null : JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

/** A stand-in for the farms API: answers the list, records every change, and lets a test choose the answer to a change. */
function fakeApi(farms: FarmDto[], answer: (request: Request) => Response | undefined = () => undefined) {
  const changes: Request[] = [];
  vi.mocked(fetch).mockImplementation((input) => {
    const request = input as Request;
    if (request.method === "GET")
      return Promise.resolve(json({ items: farms, pageNumber: 1, pageSize: 25, totalCount: farms.length, totalPages: 1, hasNext: false }));
    changes.push(request);
    return Promise.resolve(answer(request) ?? (request.method === "DELETE" ? new Response(null, { status: 204 }) : json(farms[0], 200)));
  });
  return changes;
}

const twoFarms = [
  farm({}),
  farm({
    id: "f2",
    name: "Nursery B",
    code: null,
    farmType: "nursery",
    areaHectares: null,
    orderSequence: 2,
    isActive: false,
    eTag: '"v2"',
  }),
];

async function openRowMenu(name: string) {
  await userEvent.click(await screen.findByRole("button", { name: `Actions for ${name}` }));
  return screen.findByRole("menu");
}

describe("FarmSettings", () => {
  beforeAll(async () => {
    await setupI18nForTest({
      [moduleKey]: { en },
      common: { en: { actions: { cancel: "Cancel" }, errors: { generic: "Something went wrong." } } },
    });
    setTenantForTest();
  });
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
    signInForTest([`${moduleKey}:user`]);
  });
  afterEach(() => vi.unstubAllGlobals());

  it("lists the farms in order with their type and status", async () => {
    fakeApi(twoFarms);
    render(<FarmSettings />);

    await screen.findByRole("cell", { name: "Nursery B" });
    const rows = screen.getAllByRole("row");
    expect(within(rows[1]!).getByRole("cell", { name: "Ladang Hijau" })).toBeInTheDocument();
    expect(within(rows[1]!).getByText("Greenhouse")).toBeInTheDocument();
    expect(within(rows[2]!).getByRole("cell", { name: "Nursery B" })).toBeInTheDocument();
    expect(within(rows[2]!).getByText("Inactive")).toBeInTheDocument();
  });

  it("adds a farm from the drawer", async () => {
    const changes = fakeApi([]);
    render(<FarmSettings />);
    expect(await screen.findByText("No farms yet")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Add farm" }));
    await userEvent.type(screen.getByRole("textbox", { name: /^Name/ }), "Ladang Hijau");
    await userEvent.type(screen.getByRole("textbox", { name: "Code" }), "FRM-001");
    await userEvent.type(screen.getByRole("spinbutton", { name: "Latitude" }), "3.139");
    await userEvent.type(screen.getByRole("spinbutton", { name: "Longitude" }), "101.6869");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(changes).toHaveLength(1));
    expect(changes[0]!.method).toBe("POST");
    expect(await changes[0]!.clone().json()).toMatchObject({
      name: "Ladang Hijau",
      code: "FRM-001",
      farmType: "field",
      latitude: 3.139,
      longitude: 101.6869,
      areaHectares: null,
      isActive: true,
      isExternal: true,
    });
  });

  it("shows a taken code on the code field", async () => {
    fakeApi([], () => json({ status: 409, code: "master-data.code-in-use", message: "Another farm already uses that code." }, 409));
    render(<FarmSettings />);

    await userEvent.click(await screen.findByRole("button", { name: "Add farm" }));
    await userEvent.type(screen.getByRole("textbox", { name: /^Name/ }), "Second");
    await userEvent.type(screen.getByRole("textbox", { name: "Code" }), "FRM-001");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByText("Another farm already uses that code.")).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "Code" })).toHaveAttribute("aria-invalid", "true");
  });

  it("edits a farm with the version it showed", async () => {
    const changes = fakeApi(twoFarms);
    render(<FarmSettings />);

    await userEvent.click(within(await openRowMenu("Ladang Hijau")).getByRole("menuitem", { name: "Edit" }));
    const name = screen.getByRole("textbox", { name: /^Name/ });
    expect(name).toHaveValue("Ladang Hijau");
    await userEvent.clear(name);
    await userEvent.type(name, "Ladang Biru");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(changes).toHaveLength(1));
    expect(changes[0]!.method).toBe("PUT");
    expect(changes[0]!.url).toContain("/farms/f1");
    expect(changes[0]!.headers.get("If-Match")).toBe('"v1"');
    expect(await changes[0]!.clone().json()).toMatchObject({
      name: "Ladang Biru",
      code: "FRM-001",
      farmType: "greenhouse",
      orderSequence: 1,
    });
  });

  it("moves a farm down one place, and can't move the first one up", async () => {
    const changes = fakeApi(twoFarms);
    render(<FarmSettings />);

    const menu = await openRowMenu("Ladang Hijau");
    expect(within(menu).getByRole("menuitem", { name: "Move up" })).toHaveAttribute("aria-disabled", "true");
    await userEvent.click(within(menu).getByRole("menuitem", { name: "Move down" }));

    await waitFor(() => expect(changes).toHaveLength(1));
    expect(changes[0]!.method).toBe("PUT");
    expect(changes[0]!.headers.get("If-Match")).toBe('"v1"');
    expect(await changes[0]!.clone().json()).toMatchObject({ name: "Ladang Hijau", orderSequence: 2 });
  });

  it("explains a farm someone else changed first", async () => {
    fakeApi(twoFarms, () => json({ status: 412, message: "Changed by someone else." }, 412));
    render(<FarmSettings />);

    await userEvent.click(within(await openRowMenu("Nursery B")).getByRole("menuitem", { name: "Move up" }));

    expect(await screen.findByText(/changed by someone else\. The list has been reloaded/)).toBeInTheDocument();
  });

  it("offers delete to managers only, and deletes with the version it showed", async () => {
    fakeApi(twoFarms);
    const { unmount } = render(<FarmSettings />);
    expect(within(await openRowMenu("Nursery B")).queryByRole("menuitem", { name: "Delete" })).not.toBeInTheDocument();
    unmount();

    signInForTest([`${moduleKey}:manager`]);
    const changes = fakeApi(twoFarms);
    render(<FarmSettings />);
    await userEvent.click(within(await openRowMenu("Nursery B")).getByRole("menuitem", { name: "Delete" }));
    await userEvent.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Delete" }));

    await waitFor(() => expect(changes).toHaveLength(1));
    expect(changes[0]!.method).toBe("DELETE");
    expect(changes[0]!.url).toContain("/farms/f2");
    expect(changes[0]!.headers.get("If-Match")).toBe('"v2"');
  });
});
