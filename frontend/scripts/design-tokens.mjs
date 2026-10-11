// Prints the design tokens (packages/ui-kit/src/theme/tokens.ts, the single source) as JSON.
// `scripts/generate` runs this to write DesignTokens.g.cs (Identity's sign-in pages) and docs/design-system.md.
// Needs Node 24 or later, which runs TypeScript files directly.
import { tokens } from "../packages/ui-kit/src/theme/tokens.ts";

process.stdout.write(JSON.stringify(tokens));
