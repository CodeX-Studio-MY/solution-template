import { describe, expect, it } from "vitest";
import { ApiError, readProblem } from "./problem";

describe("ApiError", () => {
  it("carries the platform code, trace and field errors from ProblemDetails", async () => {
    const response = new Response(
      JSON.stringify({
        status: 422,
        code: "access-management.group-not-found",
        message: "Add at least one line.",
        traceId: "abc",
        errors: [{ field: "lines", message: "Required" }],
      }),
      { status: 422, headers: { "Content-Type": "application/problem+json" } },
    );
    const error = await readProblem(response);
    expect(error).toBeInstanceOf(ApiError);
    expect(error.message).toBe("Add at least one line.");
    expect(error.status).toBe(422);
    expect(error.is("access-management.group-not-found")).toBe(true);
    expect(error.messageKey).toBe("errors.identity.groupNotFound");
    expect(error.forField("LINES")?.message).toBe("Required");
  });

  it("accepts the ASP.NET errors dictionary shape too", async () => {
    const error = await readProblem(new Response(JSON.stringify({ errors: { title: ["Too long", "Required"] } }), { status: 400 }));
    expect(error.fieldErrors.map((e) => e.message)).toEqual(["Too long", "Required"]);
  });

  it("survives a body that isn't JSON (a proxy's error page)", async () => {
    const error = await readProblem(new Response("<html>Bad gateway</html>", { status: 502 }));
    expect(error.status).toBe(502);
    expect(error.code).toBeUndefined();
    expect(error.message).toContain("502");
  });
});
