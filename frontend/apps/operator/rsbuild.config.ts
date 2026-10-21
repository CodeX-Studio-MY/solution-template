import { defineConfig } from "@rsbuild/core";
import { pluginReact } from "@rsbuild/plugin-react";

const gateway = process.env.GATEWAY_URL ?? "https://operator.localhost:7443";

/** The operator console: a plain single-page app (no modules to federate), served by the Gateway on operator.{BaseDomain}. */
export default defineConfig({
  plugins: [pluginReact()],
  html: { template: "./src/index.html", title: "Operator console" },
  output: { assetPrefix: "/", distPath: { root: "dist" }, cleanDistPath: true },
  // The Gateway replaces __CSP_NONCE__ on every request, as for the shell.
  security: { nonce: "__CSP_NONCE__" },
  source: { entry: { index: "./src/main.tsx" } },
  dev: { client: { overlay: false } },
  server: {
    port: 3001,
    ...(process.env.RSBUILD_PROXY === "1"
      ? {
          proxy: {
            "/api": { target: gateway, secure: false, changeOrigin: true },
            "/bff": { target: gateway, secure: false, changeOrigin: true },
          },
        }
      : {}),
  },
});
