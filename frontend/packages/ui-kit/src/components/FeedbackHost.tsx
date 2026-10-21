import Alert from "@mui/material/Alert";
import Snackbar from "@mui/material/Snackbar";
import { useTranslation } from "react-i18next";
import { useDialogStore, useSnackbarStore } from "@saaskit/web-core";
import { ConfirmDialog } from "./ConfirmDialog";

/** Shows `useSnackbar()` messages one at a time. Errors stay until dismissed; the rest go after a few seconds. */
export function SnackbarHost() {
  const { t } = useTranslation();
  const snack = useSnackbarStore((state) => state.queue[0]);
  const dismiss = useSnackbarStore((state) => state.dismiss);
  if (!snack) return null;
  const close = () => dismiss(snack.id);
  return (
    <Snackbar
      key={snack.id}
      open
      onClose={(_, reason) => reason !== "clickaway" && close()}
      autoHideDuration={snack.severity === "error" ? null : 5000}
      anchorOrigin={{ vertical: "bottom", horizontal: "center" }}
    >
      <Alert
        severity={snack.severity}
        variant="filled"
        onClose={close}
        slotProps={{ closeButton: { title: t("common:actions.dismiss"), "aria-label": t("common:actions.dismiss") } }}
        sx={{ width: "100%" }}
        role={snack.severity === "error" ? "alert" : "status"}
      >
        {snack.message}
      </Alert>
    </Snackbar>
  );
}

/** Shows `useDialog()` requests one at a time and answers the promise. */
export function DialogHost() {
  const request = useDialogStore((state) => state.queue[0]);
  const close = useDialogStore((state) => state.close);
  if (!request) return null;
  return (
    <ConfirmDialog
      key={request.id}
      open
      title={request.title}
      text={request.text}
      confirmLabel={request.confirmLabel}
      cancelLabel={request.cancelLabel}
      tone={request.tone}
      hideCancel={request.kind === "alert"}
      onConfirm={() => close(request.id, true)}
      onCancel={() => close(request.id, false)}
    />
  );
}

/** Both hosts. The shell (and the operator console) render this once, inside the theme. */
export function FeedbackHost() {
  return (
    <>
      <SnackbarHost />
      <DialogHost />
    </>
  );
}
