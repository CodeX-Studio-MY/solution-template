import { createNonceCache } from "@saaskit/ui-kit";

/** One Emotion cache for the whole app, stamped with this request's CSP nonce. Everything that renders (even the loading state) uses it. */
export const emotionCache = createNonceCache();
