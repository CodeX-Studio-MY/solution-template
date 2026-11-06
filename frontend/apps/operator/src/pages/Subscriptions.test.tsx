import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { PlatformQueryProvider } from "@saaskit/web-core";
import { setupI18nForTest } from "@saaskit/web-core/testing";
import { commonStrings, FeedbackHost } from "@saaskit/ui-kit";
import { Component as Subscriptions } from "./Subscriptions";
import en from "../i18n/operator.en.json";

const plan = (
  planId: number,
  planKey: string,
  displayName: string,
  modules: string[],
  tenantCount: number,
  monthlyPrice: number | null = null,
) => ({
  planId,
  planKey,
  displayName,
  description: null,
  monthlyPrice,
  currency: "MYR",
  sortOrder: planId,
  isActive: true,
  tenantCount,
  modules,
  eTag: `"v${planId}"`,
});

// The platform's own modules are in every plan; the product's own (here Crop Planning) are in the plans from the one that sells them up.
const platformIncluded = ["access-management", "tenant-settings", "billing"];
const plans = [
  plan(1, "lite", "Lite", platformIncluded, 12, 99),
  plan(2, "advanced", "Advanced", [...platformIncluded, "crop-planning"], 1, 299),
  plan(3, "starter", "Starter", [], 0),
];
const modules = [
  { moduleKey: "access-management", displayName: "Access Management" },
  { moduleKey: "billing", displayName: "Billing" },
  { moduleKey: "crop-planning", displayName: "Crop Planning" },
  { moduleKey: "tenant-settings", displayName: "Tenant Settings" },
];

function show() {
  const changes: string[] = [];
  vi.spyOn(globalThis, "fetch").mockImplementation(async (input, init) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    const path = new URL(request.url).pathname;
    if (request.method !== "GET") {
      changes.push(`${request.method} ${path}`);
      return Response.json(plans[0]);
    }
    return Response.json(path.endsWith("/plans") ? plans : modules);
  });
  render(
    <PlatformQueryProvider>
      <Subscriptions />
      <FeedbackHost />
    </PlatformQueryProvider>,
  );
  return changes;
}

describe("subscriptions matrix", () => {
  beforeAll(() => setupI18nForTest({ common: commonStrings, operator: { en } }));
  afterEach(() => vi.restoreAllMocks());

  it("is a table with a labelled checkbox for every module in every plan, and a price and tenant count for every plan", async () => {
    show();
    const table = await screen.findByRole("table", { name: "Modules included in each plan" });
    expect(within(table).getByRole("columnheader", { name: /Lite/ })).toHaveTextContent("12 tenants");
    expect(within(table).getByRole("columnheader", { name: /Advanced/ })).toHaveTextContent("1 tenant");
    expect(within(table).getByRole("columnheader", { name: /Lite/ })).toHaveTextContent("99.00");
    expect(within(table).getByRole("rowheader", { name: /Crop Planning/ })).toBeInTheDocument();
    expect(screen.getByRole("checkbox", { name: "Crop Planning in Lite" })).not.toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Crop Planning in Advanced" })).toBeChecked();
    expect(screen.getAllByRole("checkbox")).toHaveLength(12);
  });

  it("asks first, names how many tenants it reaches, and changes nothing when cancelled", async () => {
    const changes = show();
    await userEvent.click(await screen.findByRole("checkbox", { name: "Crop Planning in Lite" }));

    const dialog = await screen.findByRole("dialog", { name: "Add Crop Planning to Lite?" });
    expect(dialog).toHaveTextContent("12 tenants on Lite get Crop Planning straight away.");
    await userEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(changes).toEqual([]);

    await userEvent.click(screen.getByRole("checkbox", { name: "Crop Planning in Lite" }));
    await userEvent.click(
      within(await screen.findByRole("dialog", { name: "Add Crop Planning to Lite?" })).getByRole("button", { name: "Add module" }),
    );
    await waitFor(() => expect(changes).toEqual(["PUT /api/tenancy/v1/operator/plans/1/modules/crop-planning"]));
  });

  it("removes a product module from a plan after a confirm", async () => {
    const changes = show();
    await userEvent.click(await screen.findByRole("checkbox", { name: "Crop Planning in Advanced" }));
    const dialog = await screen.findByRole("dialog", { name: "Remove Crop Planning from Advanced?" });
    await userEvent.click(within(dialog).getByRole("button", { name: "Remove module" }));
    await waitFor(() => expect(changes).toEqual(["DELETE /api/tenancy/v1/operator/plans/2/modules/crop-planning"]));
  });

  it("shows the platform's modules as locked ticks in the plans that have them, and lets a plan without one add it", async () => {
    const changes = show();
    for (const name of ["Billing in Lite", "Tenant Settings in Advanced", "Access Management in Advanced"]) {
      const locked = await screen.findByRole("checkbox", { name });
      expect(locked).toBeChecked();
      expect(locked).toBeDisabled();
    }
    expect(changes).toEqual([]);

    // The tooltip says why.
    await userEvent.hover(screen.getByRole("checkbox", { name: "Billing in Lite" }).closest("span")!.parentElement!);
    expect(await screen.findByText("Included in every plan")).toBeInTheDocument();

    // A new plan has none yet, so they can be added to it.
    expect(screen.getByRole("checkbox", { name: "Billing in Starter" })).toBeEnabled();
  });
});
