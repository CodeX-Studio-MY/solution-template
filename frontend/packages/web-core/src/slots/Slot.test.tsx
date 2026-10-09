import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { signInForTest } from "../testing";
import { registerExtensions, Slot, unregisterExtensions } from "./Slot";

const props = { requestId: "1", total: { amount: 10, currency: "MYR" } } as const;

function Panel({ requestId }: { requestId: string }) {
  return <p>panel for {requestId}</p>;
}

describe("Slot", () => {
  it("renders nothing when no module fills it", () => {
    signInForTest(["purchase-request:user"]);
    const { container } = render(<Slot name="purchase-request.detail.side-panel" props={props} />);
    expect(container).toBeEmptyDOMElement();
  });

  it("renders what a module contributes, with the slot's props", async () => {
    signInForTest(["cost-center-budget:user"]);
    registerExtensions("cost-center-budget", [
      { slot: "purchase-request.detail.side-panel", component: () => Promise.resolve({ default: Panel }), requiredRole: "user" },
    ]);
    render(<Slot name="purchase-request.detail.side-panel" props={props} />);
    expect(await screen.findByText("panel for 1")).toBeInTheDocument();
    unregisterExtensions("cost-center-budget");
  });

  it("hides an extension from people below its required role", async () => {
    signInForTest(["cost-center-budget:user"]);
    registerExtensions("cost-center-budget", [
      { slot: "purchase-request.detail.side-panel", component: () => Promise.resolve({ default: Panel }), requiredRole: "manager" },
    ]);
    const { container } = render(<Slot name="purchase-request.detail.side-panel" props={props} />);
    expect(container).toBeEmptyDOMElement();
    unregisterExtensions("cost-center-budget");
  });

  it("contains a failing extension: the page around it keeps working", async () => {
    vi.spyOn(console, "error").mockImplementation(() => undefined);
    signInForTest(["cost-center-budget:user"]);
    registerExtensions("cost-center-budget", [
      {
        slot: "purchase-request.detail.side-panel",
        component: () =>
          Promise.resolve({
            default: () => {
              throw new Error("boom");
            },
          }),
        requiredRole: "user",
      },
    ]);
    render(
      <main>
        <p>host page</p>
        <Slot name="purchase-request.detail.side-panel" props={props} />
      </main>,
    );
    expect(await screen.findByText("host page")).toBeInTheDocument();
    unregisterExtensions("cost-center-budget");
  });
});
