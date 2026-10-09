import { describe, expect, it } from "vitest";
import { formatDate, formatDateTime, formatMoney, formatNumber } from "./formatters";

describe("formatters", () => {
  it("formats money in the tenant's locale and currency", () => {
    expect(formatMoney(1234.5, { locale: "en", currency: "MYR" })).toContain("1,234.50");
    expect(formatMoney("99", { locale: "en", currency: "MYR" }, "USD")).toContain("$99.00");
  });

  it("formats numbers from strings", () => {
    expect(formatNumber("1000", { locale: "en" })).toBe("1,000");
  });

  it("shows a calendar date exactly as it is, in any time zone", () => {
    expect(formatDate("2026-10-09", { locale: "en" })).toBe("Oct 9, 2026");
  });

  it("shows a moment in the tenant's time zone", () => {
    // 16:30 UTC is 00:30 the next day in Kuala Lumpur.
    expect(formatDateTime("2026-01-01T16:30:00Z", { locale: "en", timeZone: "Asia/Kuala_Lumpur" })).toContain("Jan 2, 2026");
  });
});
