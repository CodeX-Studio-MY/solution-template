import type { ComponentType } from "react";
import type { SvgIconProps } from "@mui/material/SvgIcon";
import ListAlt from "@mui/icons-material/ListAlt";
import Savings from "@mui/icons-material/Savings";
import Settings from "@mui/icons-material/Settings";
import People from "@mui/icons-material/People";
import ReceiptLong from "@mui/icons-material/ReceiptLong";
import Dashboard from "@mui/icons-material/Dashboard";
import Apps from "@mui/icons-material/Apps";

/**
 * The icons a module's menu may name (`icon: "Savings"`). A small set on purpose: importing the whole icon library would add
 * megabytes to every page. Add the icons the product needs here; an unknown name shows a generic one.
 */
const icons = { ListAlt, Savings, Settings, People, ReceiptLong, Dashboard } as const satisfies Record<string, ComponentType<SvgIconProps>>;

export type IconName = keyof typeof icons;

export function Icon({ name, ...props }: { name: string } & SvgIconProps) {
  const Component = (icons as Record<string, ComponentType<SvgIconProps>>)[name] ?? Apps;
  return <Component {...props} />;
}
