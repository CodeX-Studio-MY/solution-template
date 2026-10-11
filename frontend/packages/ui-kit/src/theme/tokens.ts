/**
 * The design tokens: the single source for every visual decision (colour, type, shape, spacing, depth, motion, density, layout).
 * This is the ONLY file in the repository that contains colour values (a lint rule fails the build on a colour literal anywhere
 * else). Everything else is derived from it:
 *   - the MUI theme (`createTenantTheme`) and every component and module, through the theme;
 *   - Identity's server-rendered sign-in pages, through `DesignTokens.g.cs` (written by `scripts/generate`);
 *   - `docs/design-system.md`, the readable reference (also written by `scripts/generate`).
 * Change a value here, run `scripts/generate`, and CI fails if a derived file is out of date.
 * A tenant may override the primary colour (see `createTenantTheme`); nothing else about the palette changes per tenant.
 *
 * Keep this file plain data (no imports, no functions): `frontend/scripts/design-tokens.mjs` loads it with Node to feed the generator.
 */
export const tokens = {
  color: {
    primary: "#1f5fbf",
    /** The primary colour where it sits on a dark surface (links and buttons on the dark scheme need a lighter blue). */
    primaryOnDark: "#7aa7ff",
    /** Text and icons on a filled primary surface. */
    onPrimary: { light: "#ffffff", dark: "#0b1a33" },
    secondary: "#5b4bb7",
    success: "#1b7a3d",
    warning: "#a35a00",
    error: "#b3261e",
    /** The error colour as text on a dark surface. */
    errorOnDark: "#ff9c94",
    info: "#00629c",
    background: { light: "#f6f7f9", paperLight: "#ffffff", dark: "#12161c", paperDark: "#1b212b" },
    text: { light: "#1a1f27", dark: "#e6e9ee", secondaryLight: "#566070", secondaryDark: "#a3acba" },
    /** Hairlines between regions (cards, table rows). Decorative, so lighter than `border`. */
    divider: { light: "#d9dde4", dark: "#2f3744" },
    /** The edge of an interactive control (an input, a checkbox). At least 3:1 against its surface. */
    border: { light: "#8a93a1", dark: "#6b7585" },
  },
  font: {
    family: '"Inter", "Roboto", "Helvetica Neue", Arial, sans-serif',
    mono: 'ui-monospace, "SFMono-Regular", Menlo, Consolas, monospace',
    /** px. `reading` is for server-rendered pages and long text, where 16px also stops mobile browsers zooming into inputs. */
    size: { small: 12, base: 14, reading: 16, h4: 16, h3: 18, h2: 22, h1: 28 },
    weight: { regular: 400, medium: 500, semibold: 600 },
    lineHeight: { body: 1.5, heading: 1.25 },
  },
  /** px. `sm` for chips and small controls, `md` for inputs, buttons and cards, `lg` for dialogs and sheets. */
  radius: { sm: 4, md: 8, lg: 12 },
  /** px. MUI's `theme.spacing(n)` is n times this: `p: 3` is 24px. */
  spacing: { unit: 8 },
  /** Depth. Two levels are enough: a raised surface (card, menu) and an overlay (dialog, popover). */
  elevation: {
    raised: "0 1px 2px rgba(16, 24, 40, 0.06), 0 1px 3px rgba(16, 24, 40, 0.1)",
    overlay: "0 12px 32px rgba(16, 24, 40, 0.16)",
  },
  /** ms and a curve. Motion is short and starts fast; users who prefer reduced motion get none (MUI honours it). */
  motion: { fast: 120, base: 200, easing: "cubic-bezier(0.2, 0, 0, 1)" },
  /** Row heights in px for tables and lists: dense for power users and long lists, comfortable otherwise. */
  density: { rowCompact: 40, rowComfortable: 52 },
  /** Layout measures in px. */
  layout: { sidebarWidth: 248, sidebarCollapsedWidth: 64, appBarHeight: 56, contentMaxWidth: 1200, pageGutter: 24 },
} as const;

export type Tokens = typeof tokens;
