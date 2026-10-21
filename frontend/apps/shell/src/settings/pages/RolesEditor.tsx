import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";
import { modules, type Role } from "@saaskit/web-core";
import { RoleSelect } from "@saaskit/ui-kit";
import { useModuleName } from "../api";

interface RolesEditorProps {
  /** Role per module key; a missing key is "no access". */
  value: Partial<Record<string, Role>>;
  onChange: (module: string, role: Role | null) => void;
  disabled?: boolean;
  /** Limit to these modules (the tenant's plan). Default: every module in platform.yaml. */
  only?: readonly string[];
}

/** One role picker per module. The modules and roles come from platform.yaml, never from a list typed here. */
export function RolesEditor({ value, onChange, disabled, only }: RolesEditorProps) {
  const moduleName = useModuleName();
  const shown = only ? modules.filter((m) => only.includes(m.key)) : modules;
  return (
    <Stack spacing={1.5}>
      {shown.map((module) => (
        <Stack key={module.key} direction="row" spacing={2} sx={{ alignItems: "center", justifyContent: "space-between" }}>
          <Typography>{moduleName(module.key)}</Typography>
          <RoleSelect
            label={moduleName(module.key)}
            value={value[module.key] ?? null}
            onChange={(role) => onChange(module.key, role)}
            disabled={disabled}
          />
        </Stack>
      ))}
    </Stack>
  );
}
