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
  routes: [],
  menu: [],
  settings: [
    {
      id: "approvals",
      label: "settings.approvals",
      path: "approvals",
      requiredRole: "admin",
      lazy: async () => ({ Component: () => <h1>Approval rules</h1> }),
    },
  ],
  i18n: { namespace: "purchasing", resources: { en: { settings: { approvals: "Approval rules" } } } },
} satisfies ModuleManifest;

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
    vi.spyOn(globalThis, "fetch").mockImplementation(async () => Response.json([]));
  });
  afterEach(() => vi.restoreAllMocks());

  it("shows the gear only to people who may open some section", async () => {
    signInForTest(["billing:user"]);
    renderAt("/");
    expect(await screen.findByRole("heading", { level: 1, name: "Welcome" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Settings" })).not.toBeInTheDocument();
  });

  it("lists only the sections the person's roles allow, including a module's own pages", async () => {
    signInForTest(["access-management:manager", "tenant-settings:user", "purchasing:admin"]);
    renderAt("/settings/users", [purchasing]);

    expect(await screen.findByRole("link", { name: "Settings" })).toHaveAttribute("href", "/settings");
    const nav = screen.getByRole("navigation", { name: "Settings sections" });
    const links = within(nav)
      .getAllByRole("link")
      .map((link) => link.textContent);
    expect(links).toEqual(["Profile", "Users", "Groups", "Devices & integrations", "Approval rules"]);
    expect(await screen.findByRole("heading", { level: 1, name: "Users" })).toBeInTheDocument();
    // A manager sees people but can't invite them (inviting needs admin, as in the API).
    expect(screen.queryByRole("button", { name: "Invite user" })).not.toBeInTheDocument();
  });

  it("refuses a section the person can't open, and sends /settings to the first allowed one", async () => {
    signInForTest(["tenant-settings:user"]);
    const router = renderAt("/settings/branding");
    expect(await screen.findByText("You don't have access to this part of Settings.")).toBeInTheDocument();

    await router.navigate("/settings");
    expect(await screen.findByRole("heading", { level: 1, name: "Profile" })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/settings/profile");
  });
});
