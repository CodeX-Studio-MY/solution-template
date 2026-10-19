import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { signInForTest } from "../testing";
import { registerExtensions, Slot, unregisterExtensions } from "./Slot";

// A slot only this test knows, in the shape a module declares one.
declare module "./slotProps" {
  interface SlotProps {
    "test.side-panel": { requestId: string };
  }
}

const props = { requestId: "1" } as const;

function Panel({ requestId }: { requestId: string }) {
  return <p>panel for {requestId}</p>;
}

describe("Slot", () => {
  it("renders nothing when no module fills it", () => {
    signInForTest(["goods-receipt:user"]);
    const { container } = render(<Slot name="test.side-panel" props={props} />);
    expect(container).toBeEmptyDOMElement();
  });

  it("renders what a module contributes, with the slot's props", async () => {
    signInForTest(["supplier-rating:user"]);
    registerExtensions("supplier-rating", [
      { slot: "test.side-panel", component: () => Promise.resolve({ default: Panel }), requiredRole: "user" },
    ]);
    render(<Slot name="test.side-panel" props={props} />);
    expect(await screen.findByText("panel for 1")).toBeInTheDocument();
    unregisterExtensions("supplier-rating");
  });

  it("hides an extension from people below its required role", async () => {
    signInForTest(["supplier-rating:user"]);
    registerExtensions("supplier-rating", [
      { slot: "test.side-panel", component: () => Promise.resolve({ default: Panel }), requiredRole: "manager" },
    ]);
    const { container } = render(<Slot name="test.side-panel" props={props} />);
    expect(container).toBeEmptyDOMElement();
    unregisterExtensions("supplier-rating");
  });

  it("contains a failing extension: the page around it keeps working", async () => {
    vi.spyOn(console, "error").mockImplementation(() => undefined);
    signInForTest(["supplier-rating:user"]);
    registerExtensions("supplier-rating", [
      {
        slot: "test.side-panel",
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
        <Slot name="test.side-panel" props={props} />
      </main>,
    );
    expect(await screen.findByText("host page")).toBeInTheDocument();
    unregisterExtensions("supplier-rating");
  });
});
