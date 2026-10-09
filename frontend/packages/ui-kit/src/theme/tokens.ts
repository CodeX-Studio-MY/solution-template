/**
 * The design tokens. This is the ONLY file in the repository that contains colour values (a lint rule fails the build on a
 * colour literal anywhere else). The theme, every component and every module use the theme, which is built from these.
 * A tenant may override the primary colour (see `createTenantTheme`); nothing else about the palette changes per tenant.
 */
export const tokens = {
  color: {
    primary: "#1f5fbf",
    secondary: "#5b4bb7",
    success: "#1b7a3d",
    warning: "#a35a00",
    error: "#b3261e",
    info: "#00629c",
    background: { light: "#f6f7f9", paperLight: "#ffffff", dark: "#12161c", paperDark: "#1b212b" },
    text: { light: "#1a1f27", dark: "#e6e9ee" },
    divider: { light: "#d9dde4", dark: "#2f3744" },
  },
  font: {
    family: '"Inter", "Roboto", "Helvetica Neue", Arial, sans-serif',
    size: { base: 14, h1: 28, h2: 22, h3: 18 },
  },
  radius: 8,
  /** Layout measures in px. */
  layout: { sidebarWidth: 248, appBarHeight: 56, contentMaxWidth: 1200 },
} as const;

export type Tokens = typeof tokens;
