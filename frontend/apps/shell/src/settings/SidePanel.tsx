import type { ReactNode } from "react";
import Drawer from "@mui/material/Drawer";

/** The detail panel of a list (a user, a group). It sits above the app bar, like a dialog, so nothing covers its top. */
export function SidePanel({ open, onClose, children }: { open: boolean; onClose: () => void; children: ReactNode }) {
  return (
    <Drawer
      anchor="right"
      open={open}
      onClose={onClose}
      sx={{ zIndex: (theme) => theme.zIndex.modal }}
      slotProps={{ paper: { sx: { width: { xs: "100%", sm: 440 }, p: 3 } } }}
    >
      {children}
    </Drawer>
  );
}
