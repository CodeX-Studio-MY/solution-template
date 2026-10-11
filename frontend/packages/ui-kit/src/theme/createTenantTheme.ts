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
  const { color, font, radius, motion, density } = tokens;

  // MUI has 25 shadow levels; the platform has two. Levels 1-7 are the raised surface, 8 and up are overlays.
  const shadows = Array.from({ length: 25 }, (_, level) =>
    level === 0 ? "none" : level < 8 ? tokens.elevation.raised : tokens.elevation.overlay,
  ) as unknown as Theme["shadows"];

  return createTheme({
    cssVariables: { colorSchemeSelector: "data" },
    colorSchemes: {
      light: {
        palette: {
          primary: { main: primary, contrastText: color.onPrimary.light },
          secondary: { main: color.secondary },
          success: { main: color.success },
          warning: { main: color.warning },
          error: { main: color.error },
          info: { main: color.info },
          background: { default: color.background.light, paper: color.background.paperLight },
          text: { primary: color.text.light, secondary: color.text.secondaryLight },
          divider: color.divider.light,
        },
      },
      dark: {
        palette: {
          // A tenant's own colour is kept as chosen; the platform default is swapped for its lighter on-dark variant.
          primary: { main: isValidColor(branding.primaryColor) ? primary : color.primaryOnDark, contrastText: color.onPrimary.dark },
          secondary: { main: color.secondary },
          error: { main: color.errorOnDark },
          background: { default: color.background.dark, paper: color.background.paperDark },
          text: { primary: color.text.dark, secondary: color.text.secondaryDark },
          divider: color.divider.dark,
        },
      },
    },
    shape: { borderRadius: radius.md },
    spacing: tokens.spacing.unit,
    shadows,
    transitions: {
      duration: { shortest: motion.fast, shorter: motion.fast, short: motion.base, standard: motion.base },
      easing: { easeInOut: motion.easing, easeOut: motion.easing },
    },
    typography: {
      fontFamily: font.family,
      fontSize: font.size.base,
      h1: { fontSize: font.size.h1, fontWeight: font.weight.semibold, lineHeight: font.lineHeight.heading },
      h2: { fontSize: font.size.h2, fontWeight: font.weight.semibold, lineHeight: font.lineHeight.heading },
      h3: { fontSize: font.size.h3, fontWeight: font.weight.semibold, lineHeight: font.lineHeight.heading },
      h4: { fontSize: font.size.h4, fontWeight: font.weight.semibold, lineHeight: font.lineHeight.heading },
      h5: { fontSize: font.size.base, fontWeight: font.weight.semibold, lineHeight: font.lineHeight.heading },
      h6: { fontSize: font.size.h4, fontWeight: font.weight.semibold, lineHeight: font.lineHeight.heading },
      body1: { fontSize: font.size.base, lineHeight: font.lineHeight.body },
      body2: { fontSize: font.size.small, lineHeight: font.lineHeight.body },
      caption: { fontSize: font.size.small },
      button: { fontSize: font.size.base, fontWeight: font.weight.medium, textTransform: "none" },
    },
    components: {
      MuiButton: { defaultProps: { disableElevation: true }, styleOverrides: { root: { borderRadius: radius.md } } },
      MuiTextField: { defaultProps: { size: "small", fullWidth: true } },
      // The app bar is a neutral surface with a hairline, so the page content and the brand colour carry the colour.
      MuiAppBar: {
        defaultProps: { color: "inherit", elevation: 0 },
        styleOverrides: {
          root: ({ theme }) => ({
            backgroundColor: (theme.vars ?? theme).palette.background.paper,
            borderBottom: `1px solid ${(theme.vars ?? theme).palette.divider}`,
          }),
        },
      },
      MuiPaper: { styleOverrides: { rounded: { borderRadius: radius.md } } },
      MuiDialog: { styleOverrides: { paper: { borderRadius: radius.lg } } },
      MuiChip: { styleOverrides: { root: { borderRadius: radius.sm, fontWeight: font.weight.medium } } },
      MuiTableRow: { styleOverrides: { root: { height: density.rowCompact } } },
      MuiTableCell: {
        styleOverrides: {
          head: ({ theme }) => ({
            fontWeight: font.weight.semibold,
            color: (theme.vars ?? theme).palette.text.secondary,
          }),
        },
      },
    },
  });
}
