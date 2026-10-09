import { render, screen } from "@testing-library/react";
import { beforeAll, describe, expect, it } from "vitest";
import { setupI18nForTest, setTenantForTest } from "@acme/web-core/testing";
import en from "../locales/en.json";
import type { RequestDto } from "../types";
import { ApprovalTimeline } from "./ApprovalTimeline";

const base: RequestDto = {
  id: "1",
  number: "PR-1",
  requesterId: "u",
  title: "t",
  description: null,
  status: "submitted",
  businessDate: "2026-10-09",
  currency: "MYR",
  total: 10,
  lines: [],
  approvals: [],
  customFields: {},
  quoteFileId: null,
  createdOn: "2026-10-09T01:00:00Z",
  submittedOn: "2026-10-09T02:00:00Z",
  decidedOn: null,
  autoApproved: false,
  rejectionReason: null,
  goodsReceivedOn: null,
  allowedActions: [],
  eTag: "v1",
};

describe("ApprovalTimeline", () => {
  beforeAll(async () => {
    await setupI18nForTest({ "purchase-request": { en } });
    setTenantForTest();
  });

  it("lists what happened, oldest first, with each approval step", () => {
    render(
      <ApprovalTimeline
        request={{
          ...base,
          status: "approved",
          decidedOn: "2026-10-10T01:00:00Z",
          approvals: [
            { level: 1, approverId: "m", approvedOn: "2026-10-09T05:00:00Z" },
            { level: 2, approverId: "a", approvedOn: "2026-10-10T01:00:00Z" },
          ],
        }}
      />,
    );
    const items = screen.getAllByRole("listitem").map((li) => li.textContent ?? "");
    expect(items[0]).toContain("Created");
    expect(items[1]).toContain("Submitted for approval");
    expect(items[2]).toContain("Approved (step 1)");
    expect(items[3]).toContain("Approved (step 2)");
    expect(items.at(-1)).toContain("Approved");
  });

  it("says why a request was rejected", () => {
    render(
      <ApprovalTimeline request={{ ...base, status: "rejected", decidedOn: "2026-10-09T09:00:00Z", rejectionReason: "Over budget" }} />,
    );
    expect(screen.getByText("Rejected")).toBeInTheDocument();
    expect(screen.getByText("Over budget")).toBeInTheDocument();
  });

  it("marks automatic approval", () => {
    render(<ApprovalTimeline request={{ ...base, status: "approved", decidedOn: "2026-10-09T02:00:00Z", autoApproved: true }} />);
    expect(screen.getByText("Approved automatically")).toBeInTheDocument();
  });
});
