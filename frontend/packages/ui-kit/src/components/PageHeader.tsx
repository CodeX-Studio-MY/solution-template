import type { ReactNode } from "react";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";

interface PageHeaderProps {
  title: string;
  subtitle?: string;
  /** Buttons on the right. */
  actions?: ReactNode;
}

/** The title row of a page. The title is the page's `h1`, which is what screen readers and the axe checks expect. */
export function PageHeader({ title, subtitle, actions }: PageHeaderProps) {
  return (
    <Stack direction="row" spacing={2} sx={{ mb: 3, alignItems: "center", justifyContent: "space-between" }}>
      <Stack>
        <Typography variant="h1" component="h1">
          {title}
        </Typography>
        {subtitle ? (
          <Typography variant="body2" color="text.secondary">
            {subtitle}
          </Typography>
        ) : null}
      </Stack>
      {actions ? (
        <Stack direction="row" spacing={1}>
          {actions}
        </Stack>
      ) : null}
    </Stack>
  );
}
