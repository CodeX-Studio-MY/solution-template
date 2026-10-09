import Chip, { type ChipProps } from "@mui/material/Chip";

export type Tone = "default" | "info" | "success" | "warning" | "error";

interface StatusChipProps {
  label: string;
  tone?: Tone;
}

/** A status as text plus colour, never colour alone. */
export function StatusChip({ label, tone = "default" }: StatusChipProps) {
  const color: ChipProps["color"] = tone;
  return <Chip size="small" label={label} color={color} variant={tone === "default" ? "outlined" : "filled"} />;
}
