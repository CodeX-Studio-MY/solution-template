// Serves the staged frontend like the Gateway does: index.html with this request's CSP nonce, the same CSP header, module
// bundles under /mfe, and a single-page fallback. APIs are not served; tests mock them in the browser.
import { createServer } from "node:http";
import { randomBytes } from "node:crypto";
import { readFile, stat } from "node:fs/promises";
import { dirname, extname, join, posix } from "node:path";
import { fileURLToPath } from "node:url";

const dist = join(dirname(fileURLToPath(import.meta.url)), "..", "dist");
const port = Number(process.env.PORT ?? 4173);
const types = { ".html": "text/html", ".js": "text/javascript", ".json": "application/json", ".css": "text/css", ".svg": "image/svg+xml" };

async function file(path) {
  try {
    return (await stat(path)).isFile() ? await readFile(path) : null;
  } catch {
    return null;
  }
}

createServer(async (request, response) => {
  const url = new URL(request.url ?? "/", `http://${request.headers.host}`);
  const nonce = randomBytes(16).toString("base64");
  const origin = request.headers.host;
  // The same policy as the Gateway's SecurityHeadersMiddleware.
  response.setHeader(
    "Content-Security-Policy",
    `default-src 'none'; script-src 'self'; style-src 'self' 'nonce-${nonce}'; img-src 'self' data: blob:; font-src 'self'; connect-src 'self' ws://${origin}; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'`,
  );
  response.setHeader("X-Content-Type-Options", "nosniff");

  // posix: a URL path always uses "/", also on Windows (where path.normalize would turn it into backslashes).
  const safe = posix.normalize(url.pathname).replace(/^(\.\.\/)+/, "");
  const target = safe.startsWith("/mfe/") ? join(dist, safe) : join(dist, "shell", safe);
  const asset = await file(target);
  if (asset && !target.endsWith("index.html")) {
    response.setHeader("Content-Type", types[extname(target)] ?? "application/octet-stream");
    response.setHeader("Cache-Control", safe.startsWith("/mfe/") ? "public, max-age=31536000, immutable" : "no-cache");
    response.end(asset);
    return;
  }
  if (url.pathname.startsWith("/api") || url.pathname.startsWith("/bff") || url.pathname.startsWith("/mfe")) {
    response.statusCode = 404;
    response.end();
    return;
  }
  const index = await file(join(dist, "shell", "index.html"));
  response.setHeader("Content-Type", "text/html");
  response.setHeader("Cache-Control", "no-store");
  response.end((index?.toString() ?? "").replaceAll("__CSP_NONCE__", nonce));
}).listen(port, () => console.log(`serving on ${port}`));
