import Paper from "@mui/material/Paper";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import Typography from "@mui/material/Typography";
import { useTranslation } from "react-i18next";
import { unwrap, useApiQuery } from "@saaskit/web-core";
import { EmptyState, PageHeader, ProblemAlert, StatusChip } from "@saaskit/ui-kit";
import { tenancy, useModuleName } from "../api";

/** The feature flags and their state for this tenant. Read-only: flags are the provider's to switch (rung 2 of the ladder). */
export function Component() {
  const { t } = useTranslation("settings");
  const moduleName = useModuleName();
  const features = useApiQuery(["tenancy", "features"], (signal) => unwrap(tenancy.GET("/api/tenancy/v1/features", { signal })));
  const rows = features.data ?? [];
  return (
    <>
      <PageHeader title={t("sections.features")} subtitle={t("features.subtitle")} />
      <ProblemAlert error={features.error ?? undefined} />
      {features.data && rows.length === 0 ? (
        <EmptyState title={t("features.empty")} />
      ) : (
        <Paper variant="outlined" sx={{ overflowX: "auto" }}>
          <Table size="small" aria-label={t("sections.features")}>
            <TableHead>
              <TableRow>
                <TableCell>{t("features.feature")}</TableCell>
                <TableCell>{t("features.module")}</TableCell>
                <TableCell>{t("features.state")}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {rows.map((feature) => (
                <TableRow key={feature.key}>
                  <TableCell>
                    <Typography variant="body2">{feature.description || feature.key}</Typography>
                    <Typography variant="caption" color="text.secondary">
                      {feature.key}
                    </Typography>
                  </TableCell>
                  <TableCell>{moduleName(feature.moduleKey)}</TableCell>
                  <TableCell>
                    <StatusChip
                      label={feature.effective ? t("common:settingsForm.on") : t("common:settingsForm.off")}
                      tone={feature.effective ? "success" : "default"}
                    />
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Paper>
      )}
    </>
  );
}
