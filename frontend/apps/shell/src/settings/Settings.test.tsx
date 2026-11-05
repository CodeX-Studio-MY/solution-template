import { render, screen, within } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { setTenantForTest, setupI18nForTest, signInForTest, TestProviders } from "@saaskit/web-core/testing";
import { commonStrings } from "@saaskit/ui-kit";
import type { ModuleManifest } from "@saaskit/web-core";
import { buildRoutes } from "../App";
import { settingsStrings } from "./strings";

const purchasing = {
  id: "purchasing",
  basePath: "/purchasing",
  routes: [{ index: true, lazy: async () => ({ Component: () => <h1>Orders</h1> }) }],
  menu: [{ label: "menu", path: "", icon: "ReceiptLong", requiredRole: "user" }],
  settings: [
    {
      id: "approvals",
      label: "settings.approvals",
      path: "approvals",
      requiredRole: "admin",
      lazy: async () => ({ Component: () => <h1>Approval rules</h1> }),
    },
  ],
  i18n: {
    namespace: "purchasing",
    resources: { en: { menu: "Purchasing", settings: { approvals: "Approval rules", autoApproveBelow: "Auto-approve below" } } },
  },
} satisfies ModuleManifest;

const purchasingSettings = {
  moduleKey: "purchasing",
  values: { autoApproveBelow: 100 },
  overrides: {},
  settings: [{ name: "autoApproveBelow", tenantEditable: true, default: 100, allowedValues: null }],
};

/** The API lists the modules the person may configure: here, purchasing for its managers and admins. Everything else is empty. */
function mockApi() {
  vi.spyOn(globalThis, "fetch").mockImplementation(async (input) => {
    const url = new URL((input as Request).url);
    const mayConfigure = ["manager", "admin"].some((role) => document.body.dataset.roles?.includes(`purchasing:${role}`));
    return Response.json(url.pathname === "/api/tenancy/v1/settings" && mayConfigure ? [purchasingSettings] : []);
  });
}

/** Signs in, and tells the fetch mock which roles the API would see. */
function signIn(roles: string[]) {
  document.body.dataset.roles = roles.join(" ");
  signInForTest(roles);
}

function renderAt(path: string, manifests: ModuleManifest[] = []) {
  const router = createMemoryRouter(buildRoutes(manifests), { initialEntries: [path] });
  render(
    <TestProviders>
      <RouterProvider router={router} />
    </TestProviders>,
  );
  return router;
}

describe("Settings", () => {
  beforeAll(() => setupI18nForTest({ common: commonStrings, settings: settingsStrings, purchasing: purchasing.i18n.resources }));
  beforeEach(() => {
    setTenantForTest();
    mockApi();
  });
  afterEach(() => vi.restoreAllMocks());

  it("shows the gear only to people who may open some section", async () => {
    signIn(["billing:user"]);
    renderAt("/");
    expect(await screen.findByRole("heading", { level: 1, name: "Welcome" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Platform settings" })).not.toBeInTheDocument();
  });

  it("lists only the platform sections the person's roles allow, and no module's pages", async () => {
    signIn(["access-management:manager", "tenant-settings:user", "purchasing:admin"]);
    renderAt("/settings/users", [purchasing]);

    expect(await screen.findByRole("link", { name: "Platform settings" })).toHaveAttribute("href", "/settings");
    const nav = screen.getByRole("navigation", { name: "Settings sections" });
    const links = within(nav)
      .getAllByRole("link")
      .map((link) => link.textContent);
    expect(links).toEqual(["Profile", "Users", "Groups", "Devices", "Applications"]);
    expect(await screen.findByRole("heading", { level: 1, name: "Users" })).toBeInTheDocument();
    // A manager sees people but can't invite them (inviting needs admin, as in the API).
    expect(screen.queryByRole("button", { name: "Invite user" })).not.toBeInTheDocument();
  });

  it("refuses a section the person can't open, and sends /settings to the first allowed one", async () => {
    signIn(["tenant-settings:user"]);
    const router = renderAt("/settings/branding");
    expect(await screen.findByText("You don't have access to this part of Settings.")).toBeInTheDocument();

    await router.navigate("/settings");
    expect(await screen.findByRole("heading", { level: 1, name: "Profile" })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/settings/profile");
  });
});

describe("A module's settings", () => {
  beforeAll(() => setupI18nForTest({ common: commonStrings, settings: settingsStrings, purchasing: purchasing.i18n.resources }));
  beforeEach(() => {
    setTenantForTest();
    mockApi();
  });
  afterEach(() => vi.restoreAllMocks());

  it("live in the module: a Settings entry under the module, read-only for its managers", async () => {
    signIn(["purchasing:manager"]);
    renderAt("/purchasing/settings", [purchasing]);

    const group = await screen.findByRole("list", { name: "Purchasing" });
    expect(within(group).getByRole("link", { name: "Settings" })).toHaveAttribute("href", "/purchasing/settings");
    expect(await screen.findByRole("heading", { level: 1, name: "Purchasing settings" })).toBeInTheDocument();
    expect(await screen.findByLabelText("Auto-approve below")).toBeDisabled();
    expect(screen.queryByRole("button", { name: "Save" })).not.toBeInTheDocument();
    // The module's own page needs admin.
    expect(screen.queryByRole("tab", { name: "Approval rules" })).not.toBeInTheDocument();
  });

  it("let the module's admin change them and open the module's own settings pages", async () => {
    signIn(["purchasing:admin"]);
    const router = renderAt("/purchasing/settings", [purchasing]);

    expect(await screen.findByLabelText("Auto-approve below")).toBeEnabled();
    expect(screen.getByRole("button", { name: "Save" })).toBeInTheDocument();
    await router.navigate("/purchasing/settings/approvals");
    expect(await screen.findByRole("heading", { level: 1, name: "Approval rules" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "General" })).toBeInTheDocument();
  });

  it("don't appear for someone who can only use the module", async () => {
    signIn(["purchasing:user"]);
    renderAt("/purchasing", [purchasing]);
    expect(await screen.findByRole("heading", { level: 1, name: "Orders" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Settings" })).not.toBeInTheDocument();
  });
});
