import js from "@eslint/js";
import boundaries from "eslint-plugin-boundaries";
import reactHooks from "eslint-plugin-react-hooks";
import globals from "globals";
import tseslint from "typescript-eslint";

/** The selectors below are the frontend's "make the wrong thing impossible or obvious" rules (SAAS_TEMPLATE_PROMPT 11). */
const colourLiteral = {
  selector: "Literal[value=/^#(?:[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$/], Literal[value=/\\b(?:rgb|rgba|hsl|hsla)\\(/]",
  message: "Colours live in packages/ui-kit/src/theme/tokens.ts only. Use a theme token (theme.palette / sx colour names).",
};
const apiLiteral = {
  selector:
    "Literal[value=/\\/api\\//]:not(CallExpression[callee.property.name=/^(GET|POST|PUT|DELETE|PATCH)$/] > Literal:first-child), TemplateElement[value.raw=/\\/api\\//]",
  message:
    'Don\'t spell /api/ paths. Call the typed client (api("service").GET("/api/...")), or use apiPath(service, path) from @farm360/web-core.',
};
const localhostLiteral = {
  selector: "Literal[value=/localhost|127\\.0\\.0\\.1/]",
  message: "No host names in code: every URL is relative (the app, the BFF and /api share one origin) and comes from configuration.",
};
const handWrittenApiType = {
  selector:
    "TSInterfaceDeclaration[id.name=/(Dto|Input)$/], TSTypeAliasDeclaration[id.name=/(Dto|Input)$/][typeAnnotation.type!='TSIndexedAccessType']",
  message: "API types are generated from the OpenAPI documents (@farm360/api-clients). Alias the generated type instead of writing one.",
};
const tenantIdentity = {
  selector: "BinaryExpression[operator=/^[!=]==?$/] > MemberExpression[property.name=/^(slug|tenantId|tenantSlug)$/]",
  message: "Never branch on tenant identity. Name the variation by what it does (a setting, a feature flag) so any tenant can choose it.",
};

export default tseslint.config(
  {
    ignores: [
      "**/dist/**",
      "**/node_modules/**",
      "**/generated/**",
      "**/playwright-report/**",
      "**/test-results/**",
      "**/.rsbuild/**",
      "**/.turbo/**",
    ],
  },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    languageOptions: { globals: { ...globals.browser, ...globals.node } },
    plugins: { "react-hooks": reactHooks, boundaries },
    settings: {
      "boundaries/elements": [
        { type: "shell", pattern: "apps/shell/**" },
        { type: "ui-kit", pattern: "packages/ui-kit/**" },
        { type: "web-core", pattern: "packages/web-core/**" },
        { type: "api-clients", pattern: "packages/api-clients/**" },
        { type: "module", pattern: "modules/*/**", capture: ["name"] },
        { type: "tooling", pattern: "tooling/**" },
        { type: "e2e", pattern: "e2e/**" },
      ],
      "import/resolver": { typescript: { alwaysTryTypes: true } },
    },
    rules: {
      ...reactHooks.configs.recommended.rules,
      "@typescript-eslint/no-explicit-any": "error",
      "@typescript-eslint/consistent-type-imports": ["error", { fixStyle: "inline-type-imports" }],
      "@typescript-eslint/no-unused-vars": ["error", { argsIgnorePattern: "^_", varsIgnorePattern: "^_" }],
      eqeqeq: ["error", "always"],
      "no-restricted-syntax": ["error", colourLiteral, apiLiteral, localhostLiteral, handWrittenApiType, tenantIdentity],
      // Modules never import each other: they share only ui-kit, web-core and api-clients, and talk through URLs and the event bus.
      "boundaries/dependencies": [
        "error",
        {
          default: "allow",
          policies: [
            { from: { element: { type: "module" } }, disallow: { to: { element: { type: "shell" } } } },
            {
              from: { element: { type: "module" } },
              disallow: { to: { element: { type: "module", captured: { name: "!{{ from.element.captured.name }}" } } } },
              message: "Modules never import other modules. Use a URL, the event bus or a slot.",
            },
            {
              from: { element: { type: "shell" } },
              disallow: { to: { element: { type: "module" } } },
              message: "The shell loads modules at runtime (Module Federation); it never imports them.",
            },
            {
              from: { element: { types: { anyOf: ["ui-kit", "web-core", "api-clients"] } } },
              disallow: { to: { element: { types: { anyOf: ["shell", "module"] } } } },
            },
            {
              from: { element: { type: "web-core" } },
              disallow: { to: { element: { type: "ui-kit" } } },
              message: "web-core is below ui-kit.",
            },
            { from: { element: { type: "api-clients" } }, disallow: { to: { element: { types: { anyOf: ["ui-kit", "web-core"] } } } } },
          ],
        },
      ],
    },
  },
  // The one file that may contain colours, and the places that legitimately name URLs and colours.
  {
    files: ["packages/ui-kit/src/theme/tokens.ts"],
    rules: { "no-restricted-syntax": ["error", apiLiteral, localhostLiteral, handWrittenApiType, tenantIdentity] },
  },
  {
    files: ["packages/web-core/src/http/**", "packages/web-core/src/tenant/**", "packages/web-core/src/session/**"],
    rules: { "no-restricted-syntax": ["error", colourLiteral, localhostLiteral, handWrittenApiType, tenantIdentity] },
  },
  {
    files: ["**/*.test.ts", "**/*.test.tsx", "**/testing/**"],
    rules: { "no-restricted-syntax": ["error", localhostLiteral, handWrittenApiType, tenantIdentity] },
  },
  {
    files: ["packages/api-clients/**", "e2e/**", "**/*.config.ts", "**/*.config.js", "tooling/**", "scripts/**"],
    rules: { "no-restricted-syntax": "off" },
  },
);
