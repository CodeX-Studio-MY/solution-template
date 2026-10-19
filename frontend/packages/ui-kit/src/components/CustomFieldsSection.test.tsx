import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { setupI18nForTest } from "@saaskit/web-core/testing";
import { CustomFieldsSection, type CustomFieldDefinition } from "./CustomFieldsSection";

const definitions: CustomFieldDefinition[] = [
  { key: "reference", label: "Reference", type: "text", required: false, displayOrder: 2 },
  { key: "cost-center", label: "Cost center", type: "list", required: true, options: ["CC-100", "CC-200"], displayOrder: 1 },
];

describe("CustomFieldsSection", () => {
  beforeAll(() => setupI18nForTest({ common: { en: { customFields: { title: "Additional information", none: "None" } } } }));
  beforeEach(() => vi.stubGlobal("fetch", vi.fn()));
  afterEach(() => vi.unstubAllGlobals());

  function respondWith(body: unknown, status = 200) {
    vi.mocked(fetch).mockResolvedValue(new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } }));
  }

  it("renders nothing for a tenant with no definitions, and asks the entity's own service", async () => {
    respondWith([]);
    const { container } = render(<CustomFieldsSection entity="goods-receipt" values={{}} onChange={() => undefined} />);
    await waitFor(() => expect(fetch).toHaveBeenCalled());
    expect(container).toBeEmptyDOMElement();
    expect(vi.mocked(fetch).mock.calls[0]![0]).toSatisfy(
      (r: Request) => new URL(r.url).pathname === "/api/goods-receipt/v1/custom-fields",
    );
  });

  it("renders the definitions in display order, marking required fields", async () => {
    respondWith(definitions);
    render(<CustomFieldsSection entity="goods-receipt" values={{}} onChange={() => undefined} />);
    const fields = await screen.findAllByRole("combobox");
    expect(fields).toHaveLength(1);
    expect(screen.getByLabelText(/Cost center/)).toBeRequired();
    const labels = screen.getAllByText(/Cost center|Reference/).map((e) => e.textContent);
    expect(labels.findIndex((l) => l?.includes("Cost center"))).toBeLessThan(labels.findIndex((l) => l?.includes("Reference")));
  });

  it("reports changes as values keyed by the field", async () => {
    respondWith(definitions);
    const onChange = vi.fn();
    render(<CustomFieldsSection entity="goods-receipt" values={{}} onChange={onChange} />);
    await userEvent.type(await screen.findByLabelText("Reference"), "A");
    expect(onChange).toHaveBeenLastCalledWith({ reference: "A" });
  });

  it("shows the server's error next to the field", async () => {
    respondWith(definitions);
    render(
      <CustomFieldsSection
        entity="goods-receipt"
        values={{}}
        onChange={() => undefined}
        errors={[{ field: "customFields.reference", message: "Too long" }]}
      />,
    );
    expect(await screen.findByText("Too long")).toBeInTheDocument();
  });
});
