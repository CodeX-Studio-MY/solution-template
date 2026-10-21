import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeAll, describe, expect, it } from "vitest";
import { getI18n, parseRoles, type ModuleManifest } from "@farm360/web-core";
import { setupI18nForTest, signInForTest } from "@farm360/web-core/testing";
import common from "../i18n/common.en.json";
import { settingsRoutes } from "../modules/routes";
import { SettingsMenu } from "./SettingsMenu";
import { settingsEntries } from "./settingsEntries";

const page = () => Promise.resolve({ Component: () => null });

const masterData = {
  id: "master-data",
  basePath: "/master-data",
  routes: [],
  menu: [],
  settings: [
    { id: "farms", group: "settings.groups.master", label: "settings.farms", path: "farms", requiredRole: "user", lazy: page },
    { id: "units", group: "settings.groups.master", label: "settings.units", path: "units", requiredRole: "admin", lazy: page },
  ],
  i18n: { namespace: "master-data" },
} satisfies ModuleManifest;

function renderMenu(roles: string[]) {
  signInForTest(roles);
  const entries = settingsEntries([masterData], parseRoles(roles), (key, options) => getI18n().t(key, options));
  return render(
    <MemoryRouter>
      <SettingsMenu entries={entries} />
    </MemoryRouter>,
  );
}

describe("SettingsMenu", () => {
  beforeAll(async () => {
    await setupI18nForTest({
      common: { en: common },
      "master-data": { en: { settings: { groups: { master: "Master" }, farms: "Farm", units: "Units" } } },
    });
  });

  it("lists the settings pages the person may open, under their group", async () => {
    renderMenu(["master-data:user"]);
    await userEvent.click(screen.getByRole("button", { name: "Settings" }));

    expect(screen.getByText("Master")).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: "Farm" })).toHaveAttribute("href", "/settings/master-data/farms");
    expect(screen.queryByRole("menuitem", { name: "Units" })).not.toBeInTheDocument();
  });

  it("is not shown to someone with no settings page to open", () => {
    renderMenu([]);
    expect(screen.queryByRole("button", { name: "Settings" })).not.toBeInTheDocument();
  });
});

describe("settingsRoutes", () => {
  it("mounts each settings page under /settings and the module's base path", () => {
    expect(settingsRoutes([masterData]).map((route) => route.path)).toEqual(["settings/master-data/farms", "settings/master-data/units"]);
  });
});
