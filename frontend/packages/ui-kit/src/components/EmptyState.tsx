import type { ReactNode } from "react";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";

export function EmptyState({ title, hint, action }: { title: string; hint?: string; action?: ReactNode }) {
  return (
    <Stack spacing={1} sx={{ py: 6, alignItems: "center" }}>
      <Typography variant="h3" component="h2">
        {title}
      </Typography>
      {hint ? (
        <Typography variant="body2" color="text.secondary">
          {hint}
        </Typography>
      ) : null}
      {action}
    </Stack>
  );
}
