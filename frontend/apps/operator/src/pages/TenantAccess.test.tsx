import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { PlatformQueryProvider } from "@saaskit/web-core";
import { setupI18nForTest } from "@saaskit/web-core/testing";
import { commonStrings } from "@saaskit/ui-kit";
import { TenantAccess, TenantLogins } from "./TenantAccess";
import en from "../i18n/operator.en.json";

const page = (items: unknown[]) =>
  Response.json({ items, pageNumber: 1, pageSize: 25, totalCount: items.length, totalPages: 1, hasNext: false });

const aisha = {
  principalId: "p1",
  type: "user",
  displayName: "Aisha",
  isActive: true,
  identifier: "aisha@example.com",
  deviceType: null,
  isExternal: null,
  status: "active",
  grantedOn: "2026-09-03T08:00:00Z",
  grantedByName: "Ops One",
};

function show(ui: React.ReactNode) {
  return render(
    <PlatformQueryProvider>
      <MemoryRouter>{ui}</MemoryRouter>
    </PlatformQueryProvider>,
  );
}

describe("tenant access", () => {
  beforeAll(() => setupI18nForTest({ common: commonStrings, operator: { en } }));
  afterEach(() => vi.restoreAllMocks());

  it("won't revoke without a reason, then sends the reason and says it doesn't change sign-in", async () => {
    const sent: unknown[] = [];
    vi.spyOn(globalThis, "fetch").mockImplementation(async (input, init) => {
      const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
      if (request.method === "PUT") {
        sent.push(await request.json());
        return Response.json({ ...aisha, status: "revoked" });
      }
      return request.url.includes("/principal-access-audit") ? page([]) : page([aisha]);
    });

    show(<TenantAccess tenantId="t1" />);
    expect(screen.getByText(en.principals.notice)).toBeInTheDocument();
    await userEvent.click(await screen.findByRole("button", { name: "Revoke Aisha" }));

    const dialog = await screen.findByRole("dialog", { name: "Revoke access for Aisha?" });
    const confirm = within(dialog).getByRole("button", { name: "Revoke access" });
    expect(confirm).toBeDisabled();
    await userEvent.type(within(dialog).getByRole("textbox", { name: /Reason/ }), "   ");
    expect(confirm).toBeDisabled();

    await userEvent.type(within(dialog).getByRole("textbox", { name: /Reason/ }), "Left the company");
    await userEvent.click(confirm);
    await waitFor(() => expect(sent).toHaveLength(1));
    expect(sent[0]).toMatchObject({ status: "revoked", reason: expect.stringContaining("Left the company") });
  });

  it("explains why login history is empty", async () => {
    vi.spyOn(globalThis, "fetch").mockImplementation(async () => page([]));
    show(<TenantLogins tenantId="t1" />);
    expect(await screen.findByText(en.logins.empty)).toBeInTheDocument();
    expect(screen.getByText(en.logins.emptyHint)).toBeInTheDocument();
  });
});
