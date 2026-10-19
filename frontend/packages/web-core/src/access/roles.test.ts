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
      "goods-receipt:user",
      "goods-receipt:manager",
      "supplier-rating:admin",
      "garbage",
      "x:nonsense",
      ":user",
    ]);
    expect(roles).toEqual({ "goods-receipt": "manager", "supplier-rating": "admin" });
  });

  it("answers 'at least this role in that module'", () => {
    const roles = parseRoles(["goods-receipt:manager"]);
    expect(canAccess(roles, "goods-receipt", "user")).toBe(true);
    expect(canAccess(roles, "goods-receipt", "admin")).toBe(false);
    expect(canAccess(roles, "supplier-rating", "user")).toBe(false);
  });
});
