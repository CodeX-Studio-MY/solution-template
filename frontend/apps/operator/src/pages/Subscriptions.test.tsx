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

const plans = [
  plan(1, "lite", "Lite", ["access-management", "tenant-settings"], 12, 99),
  plan(2, "advanced", "Advanced", ["access-management", "tenant-settings", "billing"], 1, 299),
];
const modules = [
  { moduleKey: "access-management", displayName: "Access Management" },
  { moduleKey: "billing", displayName: "Billing" },
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
    expect(within(table).getByRole("rowheader", { name: /Billing/ })).toBeInTheDocument();
    expect(screen.getByRole("checkbox", { name: "Billing in Lite" })).not.toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Billing in Advanced" })).toBeChecked();
    expect(screen.getAllByRole("checkbox")).toHaveLength(6);
  });

  it("asks first, names how many tenants it reaches, and changes nothing when cancelled", async () => {
    const changes = show();
    await userEvent.click(await screen.findByRole("checkbox", { name: "Billing in Lite" }));

    const dialog = await screen.findByRole("dialog", { name: "Add Billing to Lite?" });
    expect(dialog).toHaveTextContent("12 tenants on Lite get Billing straight away.");
    await userEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(changes).toEqual([]);

    await userEvent.click(screen.getByRole("checkbox", { name: "Billing in Lite" }));
    await userEvent.click(
      within(await screen.findByRole("dialog", { name: "Add Billing to Lite?" })).getByRole("button", { name: "Add module" }),
    );
    await waitFor(() => expect(changes).toEqual(["PUT /api/tenancy/v1/operator/plans/1/modules/billing"]));
  });

  it("warns that tenant admins lose their screens when access management or tenant settings is removed, and not for other modules", async () => {
    const changes = show();
    await userEvent.click(await screen.findByRole("checkbox", { name: "Tenant Settings in Advanced" }));
    const warned = await screen.findByRole("dialog", { name: "Remove Tenant Settings from Advanced?" });
    expect(warned).toHaveTextContent("1 tenant on Advanced loses Tenant Settings straight away");
    expect(warned).toHaveTextContent("they lose those screens");
    await userEvent.click(within(warned).getByRole("button", { name: "Remove module" }));
    await waitFor(() => expect(changes).toEqual(["DELETE /api/tenancy/v1/operator/plans/2/modules/tenant-settings"]));

    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    await userEvent.click(screen.getByRole("checkbox", { name: "Billing in Advanced" }));
    const plain = await screen.findByRole("dialog", { name: "Remove Billing from Advanced?" });
    expect(plain).not.toHaveTextContent("they lose those screens");
  });
});
