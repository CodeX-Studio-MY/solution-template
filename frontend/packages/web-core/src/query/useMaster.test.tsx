import { renderHook, waitFor } from "@testing-library/react";
import { TestProviders } from "../testing/providers";
import { useMaster } from "./useMaster";

const principals = [
  { id: "a", displayName: "Ali", type: "user" },
  { id: "b", displayName: "Bala", type: "user" },
];

describe("useMaster", () => {
  afterEach(() => vi.restoreAllMocks());

  it("loads a list once for every component that asks, typed from OpenAPI, with options and labels", async () => {
    const fetch = vi.spyOn(globalThis, "fetch").mockImplementation(async () => Response.json(principals));
    const { result } = renderHook(
      () => ({
        one: useMaster("identity", "/api/identity/v1/principals", { query: { type: "user" } }),
        two: useMaster("identity", "/api/identity/v1/principals", { query: { type: "user" } }),
      }),
      { wrapper: TestProviders },
    );
    await waitFor(() => expect(result.current.one.isLoading).toBe(false));

    expect(fetch).toHaveBeenCalledTimes(1);
    expect(new URL((fetch.mock.calls[0]![0] as Request).url).search).toBe("?type=user");
    expect(result.current.two.options).toEqual([
      { value: "a", label: "Ali" },
      { value: "b", label: "Bala" },
    ]);
    expect(result.current.one.byId("b")?.displayName).toBe("Bala");
    expect(result.current.one.labelOf("gone")).toBe("gone");
    // The item type comes from the OpenAPI document (a typo is a compile error, not an undefined at runtime).
    // @ts-expect-error not a field of PrincipalDto
    void result.current.one.items[0]?.nope;
  });

  it("reads the items of a Page<T> response", async () => {
    vi.spyOn(globalThis, "fetch").mockImplementation(async () =>
      Response.json({ items: principals, pageNumber: 1, pageSize: 100, totalCount: 2 }),
    );
    const { result } = renderHook(() => useMaster("identity", "/api/identity/v1/groups"), { wrapper: TestProviders });
    await waitFor(() => expect(result.current.items).toHaveLength(2));
  });
});
