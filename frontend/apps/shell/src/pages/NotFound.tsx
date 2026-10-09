import Button from "@mui/material/Button";
import { Link as RouterLink } from "react-router";
import { useTranslation } from "react-i18next";
import { EmptyState } from "@acme/ui-kit";

export function NotFound() {
  const { t } = useTranslation("common");
  return (
    <EmptyState
      title={t("errors.notFound")}
      hint={t("errors.notFoundHint")}
      action={
        <Button component={RouterLink} to="/">
          {t("nav.home")}
        </Button>
      }
    />
  );
}
