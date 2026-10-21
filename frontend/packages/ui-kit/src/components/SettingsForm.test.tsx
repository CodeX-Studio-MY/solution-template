import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { setupI18nForTest } from "@saaskit/web-core/testing";
import { commonStrings } from "../i18n/common";
import { SettingsForm, type SettingsView } from "./SettingsForm";

const view = {
  moduleKey: "purchase",
  values: { autoApproveBelow: 100, approvalPolicy: "single", notifyBuyer: true },
  overrides: { autoApproveBelow: 100 },
  settings: [
    { name: "autoApproveBelow", tenantEditable: true, default: 50, allowedValues: null },
    { name: "approvalPolicy", tenantEditable: false, default: "single", allowedValues: ["single", "two-level"] },
    { name: "notifyBuyer", tenantEditable: true, default: false, allowedValues: null },
  ],
} as unknown as SettingsView;

describe("SettingsForm", () => {
  beforeAll(() => setupI18nForTest({ common: commonStrings }));

  it("sends only what changed, keeps operator-only settings read-only, and resets an override with null", async () => {
    const onSave = vi.fn(async () => undefined);
    render(<SettingsForm view={view} labels={{ autoApproveBelow: { label: "Auto-approve below" } }} onSave={onSave} />);

    expect(screen.getByLabelText("approvalPolicy")).toHaveAttribute("aria-disabled", "true");
    const save = screen.getByRole("button", { name: "Save" });
    expect(save).toBeDisabled();

    await userEvent.click(screen.getByRole("switch", { name: "notifyBuyer" }));
    await userEvent.click(save);
    expect(onSave).toHaveBeenLastCalledWith({ notifyBuyer: false });

    await userEvent.click(screen.getByRole("button", { name: "Use the default: Auto-approve below" }));
    expect(onSave).toHaveBeenLastCalledWith({ autoApproveBelow: null });
  });
});
