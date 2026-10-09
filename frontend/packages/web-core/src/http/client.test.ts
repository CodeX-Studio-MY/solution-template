import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { platformFetch, setRedirect, signInUrl } from "./client";

describe("platformFetch", () => {
  const fetchMock = vi.fn<(request: Request) => Promise<Response>>();

  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal("fetch", fetchMock);
  });
  afterEach(() => vi.unstubAllGlobals());

  it("sends the CSRF header and an Idempotency-Key on a POST, and neither on a GET", async () => {
    fetchMock.mockResolvedValue(new Response("{}", { status: 200 }));
    await platformFetch(new Request("https://demo.example/api/x/v1/things", { method: "POST", body: "{}" }));
    await platformFetch(new Request("https://demo.example/api/x/v1/things"));
    const [post, get] = fetchMock.mock.calls.map(([r]) => r);
    expect(post!.headers.get("X-CSRF")).toBe("1");
    expect(post!.headers.get("Idempotency-Key")).toMatch(/^[0-9a-f-]{36}$/);
    expect(get!.headers.has("X-CSRF")).toBe(false);
    expect(get!.headers.has("Idempotency-Key")).toBe(false);
  });

  it("keeps an Idempotency-Key the caller chose, so a retry is the same request", async () => {
    fetchMock.mockResolvedValue(new Response("{}"));
    await platformFetch(new Request("https://demo.example/api/x", { method: "POST", headers: { "Idempotency-Key": "mine" }, body: "{}" }));
    expect(fetchMock.mock.calls[0]![0].headers.get("Idempotency-Key")).toBe("mine");
  });

  it("uses same-origin credentials, never tokens in JavaScript", async () => {
    fetchMock.mockResolvedValue(new Response("{}"));
    await platformFetch(new Request("https://demo.example/api/x"));
    const request = fetchMock.mock.calls[0]![0];
    expect(request.credentials).toBe("same-origin");
    expect(request.headers.has("Authorization")).toBe(false);
  });

  it("sends the browser to sign in when the session has ended", async () => {
    const redirect = vi.fn();
    setRedirect(redirect);
    fetchMock.mockResolvedValue(new Response(null, { status: 401 }));
    void platformFetch(new Request("https://demo.example/api/x"));
    await vi.waitFor(() => expect(redirect).toHaveBeenCalledTimes(1));
    expect(redirect.mock.calls[0]![0]).toMatch(/^\/bff\/login\?returnUrl=/);
  });

  it("only allows local return paths to be encoded into the sign-in URL", () => {
    expect(signInUrl("/purchase-request/1?x=1")).toBe("/bff/login?returnUrl=%2Fpurchase-request%2F1%3Fx%3D1");
  });
});
