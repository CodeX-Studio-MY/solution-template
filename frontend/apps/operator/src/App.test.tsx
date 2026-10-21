import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { setRedirect } from "@saaskit/web-core";
import { setupI18nForTest } from "@saaskit/web-core/testing";
import { commonStrings } from "@saaskit/ui-kit";
import { App } from "./App";
import { useOperatorSession } from "./session";
import en from "./i18n/operator.en.json";

describe("operator console", () => {
  beforeAll(() => setupI18nForTest({ common: commonStrings, operator: { en } }));
  beforeEach(() => {
    useOperatorSession.setState({ status: "unknown" });
    setRedirect(() => useOperatorSession.getState().signedOut());
  });
  afterEach(() => vi.restoreAllMocks());

  it("asks for email, password and code when there is no session, and signs in through the Gateway", async () => {
    const calls: Request[] = [];
    let signedIn = false;
    vi.spyOn(globalThis, "fetch").mockImplementation(async (input, init) => {
      const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
      calls.push(request);
      if (request.url.endsWith("/bff/operator/login")) {
        signedIn = true;
        return new Response(null, { status: 204 });
      }
      if (request.url.endsWith("/operator/me"))
        return signedIn ? Response.json({ id: "o1", email: "o@example.com", displayName: "Ops One" }) : new Response(null, { status: 401 });
      return Response.json([]);
    });

    render(<App />);
    await userEvent.type(await screen.findByLabelText(/Email/), "o@example.com");
    await userEvent.type(screen.getByLabelText(/Password/), "secret");
    await userEvent.type(screen.getByLabelText(/Authenticator code/), "12a3456");
    await userEvent.click(screen.getByRole("button", { name: "Sign in" }));

    const login = calls.find((c) => c.url.endsWith("/bff/operator/login"))!;
    expect(login.headers.get("X-CSRF")).toBe("1");
    expect(await login.clone().json()).toEqual({ email: "o@example.com", password: "secret", totpCode: "123456" });
    expect(await screen.findByRole("heading", { level: 1, name: "Tenants" })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByText("Ops One")).toBeInTheDocument());
  });
});
