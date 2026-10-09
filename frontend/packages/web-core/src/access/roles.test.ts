import { describe, expect, it } from "vitest";
import { atLeast, canAccess, parseRoles } from "./roles";

describe("roles", () => {
  it("orders roles lowest to highest, as platform.yaml does", () => {
    expect(atLeast("admin", "manager")).toBe(true);
    expect(atLeast("manager", "manager")).toBe(true);
    expect(atLeast("user", "manager")).toBe(false);
    expect(atLeast(null, "user")).toBe(false);
  });

  it("keeps the highest role per module and ignores anything it doesn't understand", () => {
    const roles = parseRoles([
      "purchase-request:user",
      "purchase-request:manager",
      "cost-center-budget:admin",
      "garbage",
      "x:nonsense",
      ":user",
    ]);
    expect(roles).toEqual({ "purchase-request": "manager", "cost-center-budget": "admin" });
  });

  it("answers 'at least this role in that module'", () => {
    const roles = parseRoles(["purchase-request:manager"]);
    expect(canAccess(roles, "purchase-request", "user")).toBe(true);
    expect(canAccess(roles, "purchase-request", "admin")).toBe(false);
    expect(canAccess(roles, "cost-center-budget", "user")).toBe(false);
  });
});
