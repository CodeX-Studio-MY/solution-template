import MenuItem from "@mui/material/MenuItem";
import TextField from "@mui/material/TextField";
import { useTranslation } from "react-i18next";
import { roles, type Role } from "@saaskit/web-core";

interface RoleSelectProps {
  label: string;
  value: Role | null;
  /** `null` is "no access" (the assignment is removed). */
  onChange: (role: Role | null) => void;
  disabled?: boolean;
  /** Offer "no access". Default: true. */
  allowNone?: boolean;
}

/** A role in one module. The roles and their order come from platform.yaml. */
export function RoleSelect({ label, value, onChange, disabled, allowNone = true }: RoleSelectProps) {
  const { t } = useTranslation();
  return (
    <TextField
      select
      size="small"
      label={label}
      value={value ?? ""}
      onChange={(e) => onChange(e.target.value === "" ? null : (e.target.value as Role))}
      disabled={disabled}
      sx={{ minWidth: 180 }}
    >
      {allowNone ? <MenuItem value="">{t("common:roleSelect.none")}</MenuItem> : null}
      {roles.map((role) => (
        <MenuItem key={role} value={role}>
          {t(`common:roles.${role}`)}
        </MenuItem>
      ))}
    </TextField>
  );
}
