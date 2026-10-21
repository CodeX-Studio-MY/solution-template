import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { addNamespace, initI18n, platformDefaults, setRedirect } from "@saaskit/web-core";
import { commonStrings } from "@saaskit/ui-kit";
import { App } from "./App";
import { useOperatorSession } from "./session";
import en from "./i18n/operator.en.json";
import ms from "./i18n/operator.ms.json";
import zh from "./i18n/operator.zh.json";

/** The browser's language when the platform has it. Operators have no tenant, so no tenant locale. */
function preferredLocale(): string {
  const supported: readonly string[] = platformDefaults.locales;
  return navigator.languages.map((l) => l.split("-")[0]!).find((l) => supported.includes(l)) ?? platformDefaults.locales[0];
}

// An ended (or missing) operator session shows the sign-in form; there is no tenant sign-in page to go to.
setRedirect(() => useOperatorSession.getState().signedOut());

void initI18n(preferredLocale()).then(() => {
  addNamespace("common", commonStrings);
  addNamespace("operator", { en, ms, zh });
  createRoot(document.getElementById("root")!).render(
    <StrictMode>
      <App />
    </StrictMode>,
  );
});
