import Button from "@mui/material/Button";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogContentText from "@mui/material/DialogContentText";
import DialogTitle from "@mui/material/DialogTitle";
import { useId } from "react";
import { useTranslation } from "react-i18next";

interface ConfirmDialogProps {
  open: boolean;
  title: string;
  text?: string;
  confirmLabel?: string;
  cancelLabel?: string;
  /** `danger`: the confirm button is red and Cancel gets the focus, so Enter never deletes by accident. */
  tone?: "default" | "danger";
  /** An alert has only the confirm button. */
  hideCancel?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
  busy?: boolean;
}

/** A controlled confirmation. Most screens don't render it themselves: they `await useDialog().confirm(...)`. */
export function ConfirmDialog({
  open,
  title,
  text,
  confirmLabel,
  cancelLabel,
  tone = "default",
  hideCancel,
  onConfirm,
  onCancel,
  busy,
}: ConfirmDialogProps) {
  const { t } = useTranslation();
  const titleId = useId();
  const danger = tone === "danger";
  return (
    <Dialog open={open} onClose={onCancel} aria-labelledby={titleId}>
      <DialogTitle id={titleId}>{title}</DialogTitle>
      {text ? (
        <DialogContent>
          <DialogContentText>{text}</DialogContentText>
        </DialogContent>
      ) : null}
      <DialogActions>
        {hideCancel ? null : (
          <Button onClick={onCancel} autoFocus={danger}>
            {cancelLabel ?? t("common:actions.cancel")}
          </Button>
        )}
        <Button onClick={onConfirm} disabled={busy} variant="contained" color={danger ? "error" : "primary"} autoFocus={!danger}>
          {confirmLabel ?? t("common:actions.ok")}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
