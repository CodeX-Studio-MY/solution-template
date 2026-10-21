import { useState } from "react";
import { useTranslation } from "react-i18next";
import { unwrap, useApiQuery } from "@saaskit/web-core";
import { ActivityTable, PageHeader, ProblemAlert } from "@saaskit/ui-kit";
import { identity } from "../api";

/** What can be filtered; the log may hold others, which still show. */
const kinds = [
  "sign-in",
  "sign-in-failed",
  "assignment-changed",
  "membership-changed",
  "group-changed",
  "principal-changed",
  "invitation-created",
  "invitation-accepted",
  "password-reset",
  "machine-enrolled",
  "support-access",
] as const;

/** The tenant's security log (Identity): who signed in, who changed access, and what support staff did. */
export function Component() {
  const { t } = useTranslation("settings");
  const [paging, setPaging] = useState({ page: 1, pageSize: 25 });
  const [kind, setKind] = useState("");
  const log = useApiQuery(["identity", "audit", paging, kind], (signal) =>
    unwrap(
      identity.GET("/api/identity/v1/audit", {
        params: { query: { Page: paging.page, PageSize: paging.pageSize, kind: kind || undefined } },
        signal,
      }),
    ),
  );
  const rows = log.data?.items.map((e) => ({
    id: e.id,
    occurredOn: e.occurredOn,
    kind: e.kind,
    who: e.actorName ?? e.actorId,
    onBehalfOf: e.onBehalfOf,
    subject: e.subjectName,
    details: e.details,
  }));
  return (
    <>
      <PageHeader title={t("sections.audit")} subtitle={t("audit.subtitle")} />
      <ProblemAlert error={log.error ?? undefined} />
      <ActivityTable
        label={t("sections.audit")}
        rows={rows}
        totalCount={Number(log.data?.totalCount ?? 0)}
        page={paging.page}
        pageSize={paging.pageSize}
        onPage={(page, pageSize) => setPaging({ page, pageSize })}
        kinds={kinds}
        kind={kind}
        onKind={(next) => {
          setKind(next);
          setPaging((p) => ({ ...p, page: 1 }));
        }}
      />
    </>
  );
}
