import { useState } from "react";
import { useTranslation } from "react-i18next";
import { unwrap, useApiQuery } from "@saaskit/web-core";
import { ActivityTable, PageHeader, ProblemAlert } from "@saaskit/ui-kit";
import { tenancy } from "../api";

/** Changes to the organisation itself (Tenancy): plan, applications, settings, support policy; by its admins or the provider. */
export function Component() {
  const { t } = useTranslation("settings");
  const [paging, setPaging] = useState({ page: 1, pageSize: 25 });
  const log = useApiQuery(["tenancy", "activity", paging], (signal) =>
    unwrap(tenancy.GET("/api/tenancy/v1/me/activity", { params: { query: { Page: paging.page, PageSize: paging.pageSize } }, signal })),
  );
  const rows = log.data?.items.map((e) => ({
    id: e.id,
    occurredOn: e.occurredOn,
    kind: e.kind,
    who: e.actorName ?? e.actor,
    details: e.details,
  }));
  return (
    <>
      <PageHeader title={t("sections.activity")} subtitle={t("activity.subtitle")} />
      <ProblemAlert error={log.error ?? undefined} />
      <ActivityTable
        label={t("sections.activity")}
        rows={rows}
        totalCount={Number(log.data?.totalCount ?? 0)}
        page={paging.page}
        pageSize={paging.pageSize}
        onPage={(page, pageSize) => setPaging({ page, pageSize })}
      />
    </>
  );
}
