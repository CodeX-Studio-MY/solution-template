import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { formatMoney } from "../format/formatters";
import { useTenantStore } from "../tenant/tenantStore";
import { getI18n, initI18n, setLanguage } from "./i18n";

describe("setLanguage", () => {
  beforeAll(async () => {
    await initI18n("en");
    getI18n().addResourceBundle("en", "test", { hello: "Hello" }, true, true);
    getI18n().addResourceBundle("ms", "test", { hello: "Helo" }, true, true);
  });
  beforeEach(() => {
    useTenantStore.setState({ chosenLocale: undefined, tenant: { ...useTenantStore.getState().tenant, locale: "en" } });
  });

  it("switches the strings, <html lang> and the formats the tenant store gives to dates, numbers and money, with no reload", async () => {
    await setLanguage("ms");
    expect(getI18n().t("hello", { ns: "test" })).toBe("Helo");
    expect(document.documentElement.lang).toBe("ms");
    expect(useTenantStore.getState().tenant.locale).toBe("ms");

    await setLanguage("en");
    expect(getI18n().t("hello", { ns: "test" })).toBe("Hello");
    expect(document.documentElement.lang).toBe("en");
    expect(useTenantStore.getState().tenant.locale).toBe("en");
  });

  it("is kept when the tenant loads again: the tenant's language is only the default", async () => {
    await setLanguage("zh");
    vi.spyOn(globalThis, "fetch").mockImplementation(async (input) =>
      String(input).endsWith("/bff/config")
        ? Response.json({
            tenant: { slug: "demo", name: "Demo", timeZone: "Asia/Kuala_Lumpur", currency: "MYR", locale: "en" },
            defaults: { timeZone: "Asia/Kuala_Lumpur", currency: "MYR", locales: ["en", "ms", "zh"] },
            modules: { baseUrl: "/mfe" },
            apiPrefix: "/api",
          })
        : new Response(null, { status: 404 }),
    );
    await useTenantStore.getState().load();
    const { tenant } = useTenantStore.getState();
    expect(tenant.slug).toBe("demo");
    expect(tenant.locale).toBe("zh");
    expect(formatMoney(1234.5, tenant)).toContain("1,234.50");
    vi.restoreAllMocks();
    await setLanguage("en");
  });
});
