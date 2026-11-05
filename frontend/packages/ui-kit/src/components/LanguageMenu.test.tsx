import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { getI18n, snackbar, useSnackbarStore, useTenantStore } from "@saaskit/web-core";
import { setupI18nForTest } from "@saaskit/web-core/testing";
import { commonStrings } from "../i18n/common";
import { LanguageMenu } from "./LanguageMenu";

/** What the browser calls a language in that language ("English", "Melayu", "中文"): the menu uses nothing else. */
const own = (locale: string) => new Intl.DisplayNames([locale], { type: "language" }).of(locale)!;

describe("LanguageMenu", () => {
  beforeAll(() => setupI18nForTest({ common: commonStrings }));
  beforeEach(async () => {
    await getI18n().changeLanguage("en");
    document.documentElement.lang = "en";
    useSnackbarStore.setState({ queue: [] });
    useTenantStore.setState({ chosenLocale: undefined });
  });

  it("lists every language by its own name and switches at once, then hands the choice to the app to save", async () => {
    const onChange = vi.fn(async () => undefined);
    render(<LanguageMenu onChange={onChange} />);

    await userEvent.click(screen.getByRole("button", { name: "Language" }));
    const names = screen.getAllByRole("menuitem").map((item) => item.textContent);
    expect(names).toEqual([own("en"), own("ms"), own("zh")]);
    expect(names).toContain("中文");
    expect(screen.getByRole("menuitem", { name: own("en") })).toHaveClass("Mui-selected");

    await userEvent.click(screen.getByRole("menuitem", { name: own("ms") }));
    await waitFor(() => expect(onChange).toHaveBeenCalledWith("ms"));
    expect(getI18n().language).toBe("ms");
    expect(document.documentElement.lang).toBe("ms");
    // Formats follow: the tenant store's locale is what dates, numbers and money use.
    expect(useTenantStore.getState().tenant.locale).toBe("ms");
    expect(useSnackbarStore.getState().queue).toEqual([]);
  });

  it("keeps the language for the session and says so when it could not be saved", async () => {
    render(<LanguageMenu onChange={() => Promise.reject(new Error("read-only"))} />);
    await userEvent.click(screen.getByRole("button", { name: "Language" }));
    await userEvent.click(screen.getByRole("menuitem", { name: own("zh") }));

    await waitFor(() => expect(useSnackbarStore.getState().queue).toHaveLength(1));
    expect(useSnackbarStore.getState().queue[0]).toMatchObject({ severity: "warning" });
    expect(getI18n().language).toBe("zh");
    expect(document.documentElement.lang).toBe("zh");
  });

  it("changes the language only when nothing saves it (the sign-in screen), and does nothing for the language already shown", async () => {
    const spy = vi.spyOn(snackbar, "warning");
    render(<LanguageMenu />);
    await userEvent.click(screen.getByRole("button", { name: "Language" }));
    await userEvent.click(screen.getByRole("menuitem", { name: own("en") }));
    expect(getI18n().language).toBe("en");

    await waitFor(() => expect(screen.queryByRole("menu")).not.toBeInTheDocument());
    await userEvent.click(screen.getByRole("button", { name: "Language" }));
    await userEvent.click(screen.getByRole("menuitem", { name: own("ms") }));
    await waitFor(() => expect(getI18n().language).toBe("ms"));
    expect(spy).not.toHaveBeenCalled();
  });
});
