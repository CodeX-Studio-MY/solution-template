import { Navigate, NavLink, Outlet, useLocation } from "react-router";
import Box from "@mui/material/Box";
import List from "@mui/material/List";
import ListItem from "@mui/material/ListItem";
import ListItemButton from "@mui/material/ListItemButton";
import ListItemText from "@mui/material/ListItemText";
import ListSubheader from "@mui/material/ListSubheader";
import Paper from "@mui/material/Paper";
import { useTranslation } from "react-i18next";
import { useSession } from "@saaskit/web-core";
import { EmptyState, tokens } from "@saaskit/ui-kit";
import { visibleSections, type SettingsGroup, type SettingsSection } from "./sections";

const groups: SettingsGroup[] = ["organization", "access"];

/** The Settings area: a menu of the sections this person may open, and the open section. */
export function SettingsLayout({ sections }: { sections: readonly SettingsSection[] }) {
  const { t } = useTranslation();
  const roles = useSession()?.moduleRoles ?? {};
  const { pathname } = useLocation();
  const visible = visibleSections(sections, roles);
  const current = pathname.replace(/^\/settings\/?/, "");

  if (current === "") return visible[0] ? <Navigate to={visible[0].path} replace /> : <EmptyState title={t("settings:access.denied")} />;
  const allowed = visible.some((section) => current === section.path || current.startsWith(`${section.path}/`));

  return (
    <Box sx={{ display: "flex", flexDirection: { xs: "column", md: "row" }, gap: 3, alignItems: "flex-start" }}>
      <Paper variant="outlined" component="nav" aria-label={t("settings:nav")} sx={{ width: { xs: "100%", md: 240 }, flexShrink: 0 }}>
        {groups.map((group) => {
          const entries = visible.filter((section) => section.group === group);
          if (entries.length === 0) return null;
          return (
            <List
              key={group}
              dense
              subheader={
                <ListSubheader disableSticky sx={{ bgcolor: "transparent", lineHeight: "32px" }}>
                  {t(`settings:navGroups.${group}`)}
                </ListSubheader>
              }
            >
              {entries.map((section) => (
                <ListItem key={section.path} disablePadding>
                  <ListItemButton
                    component={NavLink}
                    to={section.path}
                    sx={{
                      mx: 1,
                      borderRadius: 1,
                      "&.active": {
                        backgroundColor: "action.selected",
                        "& .MuiListItemText-primary": { fontWeight: tokens.font.weight.semibold },
                      },
                    }}
                  >
                    <ListItemText primary={t(section.label, { ns: section.ns })} />
                  </ListItemButton>
                </ListItem>
              ))}
            </List>
          );
        })}
      </Paper>
      <Box sx={{ flexGrow: 1, minWidth: 0, width: "100%" }}>
        {allowed ? <Outlet /> : <EmptyState title={t("settings:access.denied")} />}
      </Box>
    </Box>
  );
}
