import { describe, expect, it, vi } from "vitest";
import { eventBus } from "./eventBus";

describe("eventBus", () => {
  it("delivers a typed event to every listener and stops after unsubscribe", () => {
    const first = vi.fn();
    const second = vi.fn();
    const off = eventBus.on("navigate", first);
    eventBus.on("navigate", second);
    eventBus.emit("navigate", { to: "/a" });
    off();
    eventBus.emit("navigate", { to: "/b" });
    expect(first).toHaveBeenCalledTimes(1);
    expect(second).toHaveBeenCalledTimes(2);
  });

  it("isolates a failing listener from the others and from the emitter", () => {
    vi.spyOn(console, "error").mockImplementation(() => undefined);
    const good = vi.fn();
    eventBus.on("notify", () => {
      throw new Error("boom");
    });
    eventBus.on("notify", good);
    expect(() => eventBus.emit("notify", { message: "hi" })).not.toThrow();
    expect(good).toHaveBeenCalled();
  });
});
