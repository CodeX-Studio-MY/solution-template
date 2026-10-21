import { create } from "zustand";
import { queryClient, readProblem } from "@saaskit/web-core";

interface OperatorSessionState {
  status: "unknown" | "signedIn" | "signedOut";
  signedIn: () => void;
  signedOut: () => void;
}

/** Whether an operator is signed in. The session itself is an HttpOnly cookie the Gateway keeps; the page never sees a token. */
export const useOperatorSession = create<OperatorSessionState>((set) => ({
  status: "unknown",
  signedIn: () => set({ status: "signedIn" }),
  signedOut: () => {
    queryClient.clear();
    set({ status: "signedOut" });
  },
}));

const json = { "Content-Type": "application/json", "X-CSRF": "1" };

/** Signs in through the Gateway's operator BFF. Throws an ApiError (wrong details look alike, on purpose). */
export async function signIn(credentials: { email: string; password: string; totpCode: string }): Promise<void> {
  const response = await fetch("/bff/operator/login", {
    method: "POST",
    credentials: "same-origin",
    headers: json,
    body: JSON.stringify(credentials),
  });
  if (!response.ok) throw await readProblem(response);
  queryClient.clear();
  useOperatorSession.getState().signedIn();
}

export async function signOut(): Promise<void> {
  await fetch("/bff/operator/logout", { method: "POST", credentials: "same-origin", headers: json });
  useOperatorSession.getState().signedOut();
}
