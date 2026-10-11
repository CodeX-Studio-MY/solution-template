import { useId, type ReactNode } from "react";
import Paper from "@mui/material/Paper";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";

interface DashboardCardProps {
  title: string;
  /** A link or button at the right of the title, such as "View all". */
  action?: ReactNode;
  children: ReactNode;
}

/**
 * A card on the home dashboard: a titled region. Every module's widget uses it so the page reads as one surface, whoever
 * contributed each card. The title is an `h2` and names the region for screen readers.
 */
export function DashboardCard({ title, action, children }: DashboardCardProps) {
  const titleId = useId();
  return (
    <Paper
      variant="outlined"
      component="section"
      aria-labelledby={titleId}
      sx={{ p: 2.5, display: "flex", flexDirection: "column", gap: 1.5, minWidth: 0 }}
    >
      <Stack direction="row" sx={{ alignItems: "center", justifyContent: "space-between", gap: 1 }}>
        <Typography id={titleId} variant="h4" component="h2">
          {title}
        </Typography>
        {action}
      </Stack>
      {children}
    </Paper>
  );
}
