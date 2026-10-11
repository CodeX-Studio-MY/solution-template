import LinearProgress from "@mui/material/LinearProgress";
import Link from "@mui/material/Link";
import Skeleton from "@mui/material/Skeleton";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router";
import { useTranslation } from "react-i18next";
import { toNumber, unwrap, useResource, type SlotProps } from "@acme/web-core";
import { DashboardCard, MoneyText, ProblemAlert } from "@acme/ui-kit";
import { basePath, moduleKey } from "../basePath";
import { budgets } from "../client";

/** The spent share at which a budget is flagged as close to its limit; at 100% or more it is shown as an error. */
const warnAt = 0.85;

/** The home card for finance: the five budgets closest to their limit, as bars. Fills the shell's `home.dashboard` slot. */
export default function BudgetOverview(_props: SlotProps["home.dashboard"]) {
  const { t } = useTranslation(moduleKey);
  const list = useResource(() => unwrap(budgets.GET("/api/cost-center-budget/v1/budgets")), []);

  const rows = (list.data ?? [])
    .map((budget) => {
      const amount = toNumber(budget.amount);
      return { budget, amount, share: amount > 0 ? toNumber(budget.used) / amount : 0 };
    })
    .sort((a, b) => b.share - a.share)
    .slice(0, 5);

  return (
    <DashboardCard
      title={t("widgets.overview.title")}
      action={
        <Link component={RouterLink} to={basePath} variant="body2">
          {t("common:widgets.viewAll")}
        </Link>
      }
    >
      <ProblemAlert error={list.error} />
      {list.loading && !list.data ? (
        <Skeleton variant="rounded" height={96} />
      ) : rows.length === 0 ? (
        <Typography color="text.secondary">{t("widgets.overview.none")}</Typography>
      ) : (
        <Stack spacing={1.5}>
          {rows.map(({ budget, amount, share }) => (
            <Stack key={budget.code} spacing={0.5}>
              <Stack direction="row" sx={{ justifyContent: "space-between", gap: 2 }}>
                <Typography variant="h5" component="span" noWrap>
                  {budget.code}{" "}
                  <Typography component="span" variant="body2" color="text.secondary">
                    {budget.name}
                  </Typography>
                </Typography>
                <Typography variant="body2" color="text.secondary" sx={{ whiteSpace: "nowrap" }}>
                  {Math.round(share * 100)}%
                </Typography>
              </Stack>
              <LinearProgress
                variant="determinate"
                value={Math.min(100, share * 100)}
                color={share >= 1 ? "error" : share >= warnAt ? "warning" : "primary"}
                aria-label={t("widgets.overview.used", { used: String(toNumber(budget.used)), amount: String(amount) })}
              />
              <Typography variant="body2" color="text.secondary">
                <MoneyText amount={budget.used} currency={budget.currency} /> /{" "}
                <MoneyText amount={budget.amount} currency={budget.currency} />
              </Typography>
            </Stack>
          ))}
        </Stack>
      )}
    </DashboardCard>
  );
}
