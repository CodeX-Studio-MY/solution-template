import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";
import { useTranslation } from "react-i18next";
import { apiJson, useResource, type ServiceKey } from "@farm360/web-core";
import type { CustomFieldDefinition } from "./CustomFieldsSection";

interface CustomFieldsSummaryProps {
  entity: string;
  service?: ServiceKey;
  values: Readonly<Record<string, string>>;
}

/** The tenant's extra fields of one record, read-only, labelled from the definitions. Shows nothing if the tenant has none. */
export function CustomFieldsSummary({ entity, service, values }: CustomFieldsSummaryProps) {
  const { t } = useTranslation();
  const owner = (service ?? entity) as ServiceKey;
  const definitions = useResource(
    () => apiJson<CustomFieldDefinition[]>(owner, `v1/custom-fields?entity=${encodeURIComponent(entity)}`),
    [owner, entity],
  );
  const shown = [...(definitions.data ?? [])].sort((a, b) => a.displayOrder - b.displayOrder).filter((d) => values[d.key] !== undefined);
  if (shown.length === 0) return null;
  return (
    <Stack component="dl" spacing={0.5} sx={{ m: 0, mb: 2 }}>
      {shown.map((d) => (
        <Stack key={d.key} direction="row" spacing={1}>
          <Typography component="dt" variant="body2" color="text.secondary">
            {t(d.label, { defaultValue: d.label })}:
          </Typography>
          <Typography component="dd" variant="body2" sx={{ m: 0 }}>
            {values[d.key]}
          </Typography>
        </Stack>
      ))}
    </Stack>
  );
}
