import { useState } from "react";
import ToggleButton from "@mui/material/ToggleButton";
import ToggleButtonGroup from "@mui/material/ToggleButtonGroup";
import Paper from "@mui/material/Paper";
import { useTranslation } from "react-i18next";
import { unwrap, useApiQuery } from "@saaskit/web-core";
import { EmptyState, PageHeader, ProblemAlert } from "@saaskit/ui-kit";
import { feedbackKey, tenancy, type FeedbackStatus } from "../api";
import { FeedbackTable } from "../components/FeedbackTable";
import { Pager } from "../components/principals";

/** What people in every tenant sent, newest first, with the status you move each item through. */
export function Component() {
  const { t } = useTranslation("operator");
  const [status, setStatus] = useState<FeedbackStatus | "all">("all");
  const [paging, setPaging] = useState({ page: 1, pageSize: 25 });
  const list = useApiQuery([...feedbackKey, { status, ...paging }], (signal) =>
    unwrap(
      tenancy.GET("/api/tenancy/v1/operator/feedback", {
        params: { query: { status: status === "all" ? undefined : status, Page: paging.page, PageSize: paging.pageSize } },
        signal,
      }),
    ),
  );
  const items = list.data?.items;

  return (
    <>
      <PageHeader title={t("feedback.title")} subtitle={t("feedback.subtitle")} />
      <ProblemAlert error={list.error ?? undefined} />
      <ToggleButtonGroup
        exclusive
        size="small"
        color="primary"
        aria-label={t("feedback.filter")}
        value={status}
        onChange={(_, next: FeedbackStatus | "all" | null) => {
          if (!next) return;
          setStatus(next);
          setPaging((p) => ({ ...p, page: 1 }));
        }}
        sx={{
          mb: 2,
          flexWrap: "wrap",
          "& .MuiToggleButton-root": { color: "text.primary", textTransform: "none" },
          "& .MuiToggleButton-root.Mui-selected, & .MuiToggleButton-root.Mui-selected:hover": {
            bgcolor: "primary.main",
            color: "primary.contrastText",
          },
        }}
      >
        <ToggleButton value="all">{t("feedback.all")}</ToggleButton>
        {(["new", "reviewed", "done"] as const).map((value) => (
          <ToggleButton key={value} value={value}>
            {t(`common:feedback.statuses.${value}`)}
          </ToggleButton>
        ))}
      </ToggleButtonGroup>
      {items?.length === 0 ? (
        <EmptyState title={t(status === "all" ? "feedback.empty" : "feedback.noMatches")} />
      ) : (
        <Paper variant="outlined" sx={{ overflowX: "auto" }}>
          <FeedbackTable rows={items ?? []} />
          <Pager
            count={Number(list.data?.totalCount ?? 0)}
            page={paging.page}
            pageSize={paging.pageSize}
            onPage={(page, pageSize) => setPaging({ page, pageSize })}
          />
        </Paper>
      )}
    </>
  );
}
