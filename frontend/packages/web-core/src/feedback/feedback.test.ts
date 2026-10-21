import { ApiError } from "../http/problem";
import { dialog, useDialogStore } from "./dialog";
import { snackbar, useSnackbarStore } from "./snackbar";

describe("snackbar", () => {
  beforeEach(() => useSnackbarStore.setState({ queue: [] }));

  it("queues messages in order with their severity and dismisses by id", () => {
    snackbar.info("one");
    snackbar.warning("two");
    const [first, second] = useSnackbarStore.getState().queue;
    expect([first?.message, first?.severity, second?.severity]).toEqual(["one", "info", "warning"]);
    useSnackbarStore.getState().dismiss(first!.id);
    expect(useSnackbarStore.getState().queue.map((s) => s.message)).toEqual(["two"]);
  });

  it("shows the server's message for a failed call", () => {
    snackbar.fromError(new ApiError(409, { message: "That name is taken." }));
    expect(useSnackbarStore.getState().queue[0]).toMatchObject({ message: "That name is taken.", severity: "error" });
  });
});

describe("dialog", () => {
  beforeEach(() => useDialogStore.setState({ queue: [] }));

  it("resolves confirm with the answer and shows one request at a time", async () => {
    const first = dialog.confirm({ title: "Delete?", tone: "danger" });
    const second = dialog.confirm({ title: "Again?" });
    const [a, b] = useDialogStore.getState().queue;
    expect([a?.title, b?.title]).toEqual(["Delete?", "Again?"]);

    useDialogStore.getState().close(a!.id, true);
    await expect(first).resolves.toBe(true);
    useDialogStore.getState().close(b!.id, false);
    await expect(second).resolves.toBe(false);
    expect(useDialogStore.getState().queue).toEqual([]);
  });

  it("resolves alert once it is closed", async () => {
    const done = dialog.alert({ title: "Saved" });
    useDialogStore.getState().close(useDialogStore.getState().queue[0]!.id, true);
    await expect(done).resolves.toBeUndefined();
  });
});
