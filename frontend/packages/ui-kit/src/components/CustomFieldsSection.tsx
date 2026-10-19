import MenuItem from "@mui/material/MenuItem";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { useTranslation } from "react-i18next";
import { apiJson, useResource, type FieldError, type ServiceKey } from "@saaskit/web-core";

/** One extra field a tenant defined for an entity. The server validates the values; this only renders and collects them. */
export interface CustomFieldDefinition {
  key: string;
  /** An i18n key, or plain text if the tenant typed one. */
  label: string;
  type: "text" | "number" | "date" | "list";
  required: boolean;
  options?: string[];
  displayOrder: number;
}

/** Values are strings on the wire (a number field sends its digits); the server parses and validates them. */
export type CustomFieldValues = Record<string, string | null>;

interface CustomFieldsSectionProps {
  /** The entity whose fields to show (for example `goods-receipt`). */
  entity: string;
  /** The service that owns the definitions (default: the entity key, which is the service key for module entities). */
  service?: ServiceKey;
  values: CustomFieldValues;
  onChange: (values: CustomFieldValues) => void;
  /** Server-side field errors, matched by field key. */
  errors?: FieldError[];
  disabled?: boolean;
}

/**
 * The tenant's extra fields for an entity. Driven entirely by the definitions API, so there is no tenant-specific code in
 * the frontend: a tenant with no definitions sees nothing at all.
 */
export function CustomFieldsSection({ entity, service, values, onChange, errors = [], disabled }: CustomFieldsSectionProps) {
  const { t } = useTranslation();
  const owner = (service ?? entity) as ServiceKey;
  const definitions = useResource(
    () => apiJson<CustomFieldDefinition[]>(owner, `v1/custom-fields?entity=${encodeURIComponent(entity)}`),
    [owner, entity],
  );

  const fields = [...(definitions.data ?? [])].sort((a, b) => a.displayOrder - b.displayOrder);
  if (fields.length === 0) return null;

  return (
    <Stack spacing={2} component="section" aria-labelledby={`custom-fields-${entity}`}>
      <Typography id={`custom-fields-${entity}`} component="h2" variant="h3">
        {t("common:customFields.title")}
      </Typography>
      {fields.map((field) => {
        const label = t(field.label, { defaultValue: field.label });
        const error = errors.find((e) => e.field.toLowerCase().endsWith(field.key.toLowerCase()));
        const value = values[field.key] ?? "";
        const common = {
          label,
          required: field.required,
          disabled,
          error: error !== undefined,
          helperText: error?.message,
          id: `custom-${field.key}`,
        };
        const set = (next: string) => onChange({ ...values, [field.key]: next === "" ? null : next });
        return field.type === "list" ? (
          <TextField key={field.key} {...common} select value={value} onChange={(e) => set(e.target.value)}>
            {!field.required ? <MenuItem value="">{t("common:customFields.none")}</MenuItem> : null}
            {(field.options ?? []).map((option) => (
              <MenuItem key={option} value={option}>
                {option}
              </MenuItem>
            ))}
          </TextField>
        ) : (
          <TextField
            key={field.key}
            {...common}
            type={field.type === "number" ? "number" : field.type === "date" ? "date" : "text"}
            slotProps={field.type === "date" ? { inputLabel: { shrink: true } } : undefined}
            value={value}
            onChange={(e) => set(e.target.value)}
          />
        );
      })}
    </Stack>
  );
}
