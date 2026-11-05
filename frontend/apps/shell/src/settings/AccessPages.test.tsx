import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter, RouterProvider } from "react-router";
import { setTenantForTest, setupI18nForTest, signInForTest, TestProviders } from "@saaskit/web-core/testing";
import { commonStrings } from "@saaskit/ui-kit";
import { buildRoutes } from "../App";
import { settingsStrings } from "./strings";

const principal = (id: string, displayName: string, type: "user" | "machine" | "service", extra: object = {}) => ({
  id,
  displayName,
  type,
  email: type === "user" ? `${displayName.toLowerCase()}@example.com` : null,
  clientId: type === "user" ? null : `client-${id}`,
  status: "active",
  isActive: true,
  eTag: '"v1"',
  ...extra,
});

const everyone = [principal("u1", "Ali", "user"), principal("m1", "Packing tablet", "machine"), principal("s1", "Partner ERP", "service")];

function mockApi(seen: string[]) {
  vi.spyOn(globalThis, "fetch").mockImplementation(async (input) => {
    const url = new URL((input as Request).url);
    seen.push(url.pathname + url.search);
    if (url.pathname === "/api/identity/v1/principals") {
      const type = url.searchParams.get("type");
      return Response.json(type ? everyone.filter((p) => p.type === type) : everyone);
    }
    if (url.pathname === "/api/identity/v1/groups")
      return Response.json([{ id: "g1", name: "Packers", isActive: true, memberCount: 0, eTag: '"v1"' }]);
    return Response.json([]);
  });
}

function renderAt(path: string) {
  render(
    <TestProviders>
      <RouterProvider router={createMemoryRouter(buildRoutes([]), { initialEntries: [path] })} />
    </TestProviders>,
  );
}

describe("Devices, applications and groups", () => {
  beforeAll(() => setupI18nForTest({ common: commonStrings, settings: settingsStrings }));
  beforeEach(() => {
    setTenantForTest();
    signInForTest(["access-management:admin"]);
  });
  afterEach(() => vi.restoreAllMocks());

  it("lists only devices on the Devices page, and only applications on the Applications page", async () => {
    const seen: string[] = [];
    mockApi(seen);
    renderAt("/settings/devices");
    expect(await screen.findByRole("button", { name: "Packing tablet" }, { timeout: 10_000 })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Partner ERP" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Add device" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Add application" })).not.toBeInTheDocument();
    expect(seen).toContain("/api/identity/v1/principals?type=machine");
  });

  it("lists only applications on the Applications page", async () => {
    mockApi([]);
    renderAt("/settings/applications");
    expect(await screen.findByRole("button", { name: "Partner ERP" }, { timeout: 10_000 })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Packing tablet" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Add application" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Add device" })).not.toBeInTheDocument();
  });

  it("offers users, devices and applications, each with its type, when adding a member to a group", async () => {
    mockApi([]);
    renderAt("/settings/groups");
    await userEvent.click(await screen.findByRole("button", { name: "Packers" }, { timeout: 10_000 }));
    await userEvent.click(await screen.findByRole("combobox", { name: "Add a member" }));
    const options = within(await screen.findByRole("listbox")).getAllByRole("option");
    expect(options.map((o) => o.textContent)).toEqual(["Ali (User)", "Packing tablet (Device)", "Partner ERP (Application)"]);
  });
});
