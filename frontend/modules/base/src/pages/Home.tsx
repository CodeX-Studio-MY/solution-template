import { useTranslation } from "react-i18next";
import { EmptyState, PageHeader } from "@saaskit/ui-kit";

/**
 * The module ships empty: a product's master data (farms, crops, statuses, ...) is managed here. Replace this page with the
 * product's first master list (docs/adding-a-master-entity.md).
 */
export function Component() {
  const { t } = useTranslation("base");
  return (
    <>
      <PageHeader title={t("home.title")} />
      <EmptyState title={t("home.empty")} hint={t("home.emptyHint")} />
    </>
  );
}
