import createCache, { type EmotionCache } from "@emotion/cache";

/**
 * Emotion writes styles into `<style>` tags. The Gateway's CSP allows styles only with a per-request nonce, which it puts in
 * a `<meta name="csp-nonce">` tag of index.html; this cache stamps every style tag with it. Created once by the shell.
 */
export function createNonceCache(): EmotionCache {
  const nonce = document.querySelector<HTMLMetaElement>('meta[name="csp-nonce"]')?.content;
  return createCache({ key: "css", ...(nonce && nonce !== "__CSP_NONCE__" ? { nonce } : {}) });
}
