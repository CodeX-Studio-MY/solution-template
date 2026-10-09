import { createTheme, type Theme } from "@mui/material/styles";
import { tokens } from "./tokens";

export interface TenantBranding {
  primaryColor?: string;
}

const hex = /^#(?:[0-9a-f]{3}|[0-9a-f]{6})$/i;

/** Whether a tenant-supplied colour is safe to use (a plain hex colour). Anything else is ignored. */
export function isValidColor(value: string | undefined): value is string {
  return value !== undefined && hex.test(value);
}

/**
 * The MUI theme: CSS variables on (so the tenant's colour changes without re-rendering), light and dark schemes from the
 * tokens, and the tenant's primary colour on top. Components use theme tokens only, never colours.
 */
export function createTenantTheme(branding: TenantBranding = {}): Theme {
  const primary = isValidColor(branding.primaryColor) ? branding.primaryColor : tokens.color.primary;
  return createTheme({
    cssVariables: { colorSchemeSelector: "data" },
    colorSchemes: {
      light: {
        palette: {
          primary: { main: primary },
          secondary: { main: tokens.color.secondary },
          success: { main: tokens.color.success },
          warning: { main: tokens.color.warning },
          error: { main: tokens.color.error },
          info: { main: tokens.color.info },
          background: { default: tokens.color.background.light, paper: tokens.color.background.paperLight },
          text: { primary: tokens.color.text.light },
          divider: tokens.color.divider.light,
        },
      },
      dark: {
        palette: {
          primary: { main: primary },
          secondary: { main: tokens.color.secondary },
          background: { default: tokens.color.background.dark, paper: tokens.color.background.paperDark },
          text: { primary: tokens.color.text.dark },
          divider: tokens.color.divider.dark,
        },
      },
    },
    shape: { borderRadius: tokens.radius },
    typography: {
      fontFamily: tokens.font.family,
      fontSize: tokens.font.size.base,
      h1: { fontSize: tokens.font.size.h1, fontWeight: 600 },
      h2: { fontSize: tokens.font.size.h2, fontWeight: 600 },
      h3: { fontSize: tokens.font.size.h3, fontWeight: 600 },
    },
    components: {
      MuiButton: { defaultProps: { disableElevation: true } },
      MuiTextField: { defaultProps: { size: "small", fullWidth: true } },
    },
  });
}
