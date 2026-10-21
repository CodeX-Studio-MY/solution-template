import { useId, type FormEvent, type ReactNode } from "react";
import Button from "@mui/material/Button";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogContentText from "@mui/material/DialogContentText";
import DialogTitle from "@mui/material/DialogTitle";
import Stack from "@mui/material/Stack";
import { useTranslation } from "react-i18next";

interface FormDialogProps {
  open: boolean;
  title: string;
  text?: string;
  submitLabel?: string;
  busy?: boolean;
  onSubmit: () => void | Promise<unknown>;
  onClose: () => void;
  children: ReactNode;
  maxWidth?: "xs" | "sm" | "md";
}

/** A small form in a dialog (create, rename, invite). Enter submits; the fields are the children. */
export function FormDialog({ open, title, text, submitLabel, busy, onSubmit, onClose, children, maxWidth = "xs" }: FormDialogProps) {
  const { t } = useTranslation();
  const titleId = useId();
  async function submit(event: FormEvent) {
    event.preventDefault();
    await onSubmit();
  }
  return (
    <Dialog open={open} onClose={onClose} aria-labelledby={titleId} fullWidth maxWidth={maxWidth}>
      <form onSubmit={submit} noValidate>
        <DialogTitle id={titleId}>{title}</DialogTitle>
        <DialogContent>
          {text ? <DialogContentText sx={{ mb: 2 }}>{text}</DialogContentText> : null}
          <Stack spacing={2} sx={{ pt: 1 }}>
            {children}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>{t("common:actions.cancel")}</Button>
          <Button type="submit" variant="contained" disabled={busy}>
            {submitLabel ?? t("common:actions.save")}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}
