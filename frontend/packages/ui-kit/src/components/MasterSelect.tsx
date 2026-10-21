import Autocomplete from "@mui/material/Autocomplete";
import TextField from "@mui/material/TextField";
import { useTranslation } from "react-i18next";
import type { Master, MasterOption } from "@saaskit/web-core";

interface MasterSelectProps {
  /** From `useMaster(...)`. */
  master: Pick<Master<unknown>, "options" | "isLoading" | "error">;
  label: string;
  value: string | null;
  onChange: (value: string | null) => void;
  required?: boolean;
  disabled?: boolean;
  /** A server-side field error, shown under the field. */
  error?: string;
  helperText?: string;
}

/** A searchable picker over master data. Keeps the id as the value and shows the label. */
export function MasterSelect({ master, label, value, onChange, required, disabled, error, helperText }: MasterSelectProps) {
  const { t } = useTranslation();
  const selected = master.options.find((option) => option.value === value) ?? null;
  return (
    <Autocomplete<MasterOption>
      options={master.options}
      value={selected}
      onChange={(_, option) => onChange(option?.value ?? null)}
      getOptionLabel={(option) => option.label}
      isOptionEqualToValue={(a, b) => a.value === b.value}
      loading={master.isLoading}
      loadingText={t("common:master.loading")}
      noOptionsText={t("common:master.noOptions")}
      disabled={disabled}
      renderInput={(params) => (
        <TextField
          {...params}
          label={label}
          required={required}
          error={Boolean(error) || Boolean(master.error)}
          helperText={error ?? (master.error ? t("common:master.failed") : helperText)}
        />
      )}
    />
  );
}
