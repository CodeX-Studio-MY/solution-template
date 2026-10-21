import { ApiError } from "../http/problem";
import { dialog, useDialogStore } from "./dialog";
import { snackbar, useSnackbarStore } from "./snackbar";

describe("snackbar", () => {
  beforeEach(() => useSnackbarStore.setState({ queue: [] }));

  it("shows the latest message at once, but keeps errors until they are dismissed", () => {
    snackbar.error("failed");
    snackbar.info("one");
    snackbar.success("two");
    const queue = useSnackbarStore.getState().queue;
    expect(queue.map((s) => [s.message, s.severity])).toEqual([
      ["failed", "error"],
      ["two", "success"],
    ]);
    useSnackbarStore.getState().dismiss(queue[0]!.id);
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
