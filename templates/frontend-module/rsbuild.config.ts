import { defineConfig } from "@rsbuild/core";
import { pluginReact } from "@rsbuild/plugin-react";
import { pluginModuleFederation } from "@module-federation/rsbuild-plugin";
import { sharedSingletons } from "@farm360/build-config/federation";

const gateway = process.env.GATEWAY_URL ?? "https://localhost:7443";

export default defineConfig(({ command }) => ({
  plugins: [
    pluginReact(),
    pluginModuleFederation({
      // The remote's name is the module key; the shell loads `__KEY__/manifest`.
      name: "__KEY__",
      filename: "remoteEntry.js",
      // A module key has hyphens, which is not a valid variable name; assign the container on the global object instead.
      library: { type: "global", name: "__KEY__" },
      exposes: { "./manifest": "./src/manifest.ts" },
      shared: sharedSingletons(import.meta.dirname),
      dts: false,
      manifest: true,
    }),
  ],
  // `pnpm dev` here runs the module on its own in a small dev shell (src/standalone), against a running Gateway.
  // The federated build has no app of its own; only `dev` runs the standalone shell.
  source: { entry: { index: command === "dev" ? "./src/standalone/main.ts" : "./src/entry.ts" } },
  html: { template: "./src/standalone/index.html" },
  output: { assetPrefix: "auto", distPath: { root: "dist" }, cleanDistPath: true },
  security: { nonce: "__CSP_NONCE__" },
  server: {
    port: __PORT__,
    proxy: {
      "/api": { target: gateway, secure: false, changeOrigin: false },
      "/bff": { target: gateway, secure: false, changeOrigin: false },
    },
  },
  dev: { client: { overlay: false } },
}));
