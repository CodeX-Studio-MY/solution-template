import { describe, expect, it } from "vitest";
import { createTenantTheme as create, isValidColor, type TenantBranding } from "./createTenantTheme";
import { tokens } from "./tokens";

interface Schemes {
  colorSchemes: Record<"light" | "dark", { palette: { primary: { main: string } } } | undefined>;
}

/** The theme with its colour schemes visible (MUI types them on a separate CSS-variables theme type). */
function createTenantTheme(branding?: TenantBranding) {
  return create(branding) as unknown as Schemes & ReturnType<typeof create>;
}

describe("createTenantTheme", () => {
  it("uses the platform primary colour by default", () => {
    expect(createTenantTheme().colorSchemes.light?.palette.primary.main).toBe(tokens.color.primary);
  });

  it("lets a tenant override the primary colour, in both colour schemes", () => {
    const theme = createTenantTheme({ primaryColor: "#aa3300" });
    expect(theme.colorSchemes.light?.palette.primary.main).toBe("#aa3300");
    expect(theme.colorSchemes.dark?.palette.primary.main).toBe("#aa3300");
  });

  it("ignores a colour that isn't a plain hex value (it can't carry CSS into the page)", () => {
    for (const bad of ["red", "url(javascript:alert(1))", "#12", "#gggggg", "#123456; background:red", ""]) {
      expect(isValidColor(bad)).toBe(false);
      expect(createTenantTheme({ primaryColor: bad }).colorSchemes.light?.palette.primary.main).toBe(tokens.color.primary);
    }
  });

  it("turns CSS variables on, so a tenant's colour doesn't re-render components", () => {
    expect(createTenantTheme().vars).toBeTruthy();
  });
});
