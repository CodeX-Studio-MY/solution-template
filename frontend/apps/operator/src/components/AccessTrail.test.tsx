import { render, screen } from "@testing-library/react";
import { setupI18nForTest } from "@saaskit/web-core/testing";
import { AccessTrail } from "./AccessTrail";
import en from "../i18n/operator.en.json";
import type { AccessAuditRow, AccessStatus } from "../api";

function row(id: number, action: AccessAuditRow["action"], toStatus: AccessStatus, reason: string | null = null): AccessAuditRow {
  return {
    id,
    occurredOn: "2026-11-02T08:00:00Z",
    tenantId: "t1",
    tenantName: "Acme",
    principalId: "p1",
    principalName: "North Gate",
    action,
    fromStatus: null,
    toStatus,
    actorId: "o1",
    actorName: "Ops One",
    reason,
    remoteAddress: null,
  };
}

describe("AccessTrail", () => {
  beforeAll(() => setupI18nForTest({ operator: { en } }));

  it("says who, when and why in words, newest first", () => {
    render(<AccessTrail rows={[row(2, "suspended", "suspended", "Gateway returned for repair"), row(1, "granted", "active")]} />);
    const entries = screen.getAllByRole("listitem");
    expect(entries[0]).toHaveTextContent("Suspended");
    expect(entries[0]).toHaveTextContent("Ops One");
    expect(entries[0]).toHaveTextContent("Gateway returned for repair");
    expect(entries[1]).toHaveTextContent("Granted");
  });

  it("draws the state that lasted between two entries, and no line while access was revoked", () => {
    const { container } = render(
      <AccessTrail
        rows={[row(4, "granted", "active"), row(3, "revoked", "revoked"), row(2, "suspended", "suspended"), row(1, "granted", "active")]}
      />,
    );
    const lines = [...container.querySelectorAll("[data-line]")].map((line) => line.getAttribute("data-line"));
    // Below the newest grant it was revoked (no line), below the revoke it was suspended, below the suspension it was active.
    expect(lines).toEqual(["suspended", "active"]);
    expect(container.querySelector('[data-state="revoked"]')).toBeInTheDocument();
  });
});
