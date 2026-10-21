import { useState } from "react";
import { Link as RouterLink } from "react-router";
import IconButton from "@mui/material/IconButton";
import ListSubheader from "@mui/material/ListSubheader";
import Menu from "@mui/material/Menu";
import MenuItem from "@mui/material/MenuItem";
import Tooltip from "@mui/material/Tooltip";
import { useTranslation } from "react-i18next";
import { Icon } from "@farm360/ui-kit";
import { groupSettings, type SettingsEntry } from "./settingsEntries";

/**
 * The gear in the top bar: base data and configuration that modules contribute as settings pages, grouped (Master → Farm).
 * Not rendered when the person can open none of them.
 */
export function SettingsMenu({ entries }: { entries: readonly SettingsEntry[] }) {
  const { t } = useTranslation();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  if (entries.length === 0) return null;
  const close = () => setAnchor(null);

  return (
    <>
      <Tooltip title={t("common:nav.settings")}>
        <IconButton
          aria-label={t("common:nav.settings")}
          aria-haspopup="menu"
          aria-expanded={anchor ? true : undefined}
          onClick={(e) => setAnchor(e.currentTarget)}
        >
          <Icon name="Settings" />
        </IconButton>
      </Tooltip>
      <Menu anchorEl={anchor} open={Boolean(anchor)} onClose={close} slotProps={{ list: { dense: true } }}>
        {/* Menu takes its items as direct children (no fragments), so each group is flattened into its heading and items. */}
        {groupSettings(entries).flatMap(({ group, entries: grouped }) => [
          <ListSubheader
            key={`group:${group}`}
            disableSticky
            sx={{ bgcolor: "transparent", lineHeight: "32px", textTransform: "uppercase" }}
          >
            {group}
          </ListSubheader>,
          ...grouped.map((entry) => (
            <MenuItem key={entry.to} component={RouterLink} to={entry.to} onClick={close} sx={{ pl: 3 }}>
              {entry.label}
            </MenuItem>
          )),
        ])}
      </Menu>
    </>
  );
}
