import { defineConfig } from "@rsbuild/core";
import { pluginReact } from "@rsbuild/plugin-react";
import { pluginModuleFederation } from "@module-federation/rsbuild-plugin";
import { sharedSingletons } from "@saaskit/build-config/federation";

const gateway = process.env.GATEWAY_URL ?? "https://localhost:7443";

export default defineConfig({
  plugins: [
    pluginReact(),
    pluginModuleFederation({
      name: "shell",
      // No static remotes: which modules a tenant has is data, so remotes are registered at runtime from /api/tenancy/v1/me/modules.
      remotes: {},
      shared: sharedSingletons(import.meta.dirname),
      dts: false,
      manifest: true,
    }),
  ],
  html: { template: "./src/index.html", title: "App" },
  // The shell is served from the site root on every route, so its assets must be absolute. (Modules use "auto": relative to their manifest.)
  output: { assetPrefix: "/", distPath: { root: "dist" }, cleanDistPath: true },
  // The Gateway replaces __CSP_NONCE__ on every request; the CSP allows only nonce'd inline styles and same-origin scripts.
  security: { nonce: "__CSP_NONCE__" },
  server: { port: 3000 },
  dev: { client: { overlay: false } },
  // `pnpm dev` runs the shell with live reload and sends /api and /bff to a running Gateway (same origin, so cookies work).
  source: { entry: { index: "./src/main.ts" } },
  tools: {
    rspack: { output: { uniqueName: "shell" } },
  },
  ...(process.env.RSBUILD_PROXY === "1"
    ? {
        server: {
          port: 3000,
          proxy: {
            "/api": { target: gateway, secure: false, changeOrigin: false },
            "/bff": { target: gateway, secure: false, changeOrigin: false },
            "/mfe": { target: gateway, secure: false, changeOrigin: false },
          },
        },
      }
    : {}),
});
