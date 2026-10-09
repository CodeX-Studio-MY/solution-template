import Alert from "@mui/material/Alert";
import Paper from "@mui/material/Paper";
import Typography from "@mui/material/Typography";
import { useTranslation } from "react-i18next";
import { ApiError, toNumber, unwrap, useResource, type SlotProps } from "@acme/web-core";
import { MoneyText } from "@acme/ui-kit";
import { budgets } from "../client";

/**
 * The panel this module puts on the purchase request page (rung 6). It receives exactly the props the slot declares in
 * `@acme/web-core` and loads its own data: the purchase-request module doesn't know it exists.
 */
export default function RemainingBudget({ costCenter, total }: SlotProps["purchase-request.detail.side-panel"]) {
  const { t } = useTranslation("cost-center-budget");
  const budget = useResource(
    () =>
      costCenter
        ? unwrap(budgets.GET("/api/cost-center-budget/v1/budgets/{code}", { params: { path: { code: costCenter } } }))
        : Promise.resolve(undefined),
    [costCenter],
  );

  if (!costCenter) return null;
  // A cost center without a budget isn't an error worth showing on someone else's page.
  if (budget.error instanceof ApiError && budget.error.status === 404) return null;
  if (!budget.data) return null;

  const over = toNumber(budget.data.remaining) < total.amount;
  return (
    <Paper variant="outlined" sx={{ p: 2 }} component="section" aria-labelledby="remaining-budget">
      <Typography id="remaining-budget" variant="h3" component="h2" sx={{ mb: 1 }}>
        {t("panel.title", { code: budget.data.code })}
      </Typography>
      <Typography>
        {t("panel.remaining")}: <MoneyText amount={budget.data.remaining} currency={budget.data.currency} />
      </Typography>
      <Typography variant="body2" color="text.secondary">
        {t("panel.of")} <MoneyText amount={budget.data.amount} currency={budget.data.currency} />
      </Typography>
      {over ? (
        <Alert severity="warning" sx={{ mt: 1 }}>
          {t("panel.over")}
        </Alert>
      ) : null}
    </Paper>
  );
}
