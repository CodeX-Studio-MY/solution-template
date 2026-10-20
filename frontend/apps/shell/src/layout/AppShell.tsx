import { useState } from "react";
import { Link as RouterLink, NavLink, Outlet, useLocation } from "react-router";
import AppBar from "@mui/material/AppBar";
import Box from "@mui/material/Box";
import Breadcrumbs from "@mui/material/Breadcrumbs";
import Divider from "@mui/material/Divider";
import Drawer from "@mui/material/Drawer";
import IconButton from "@mui/material/IconButton";
import Link from "@mui/material/Link";
import List from "@mui/material/List";
import ListItem from "@mui/material/ListItem";
import ListItemButton from "@mui/material/ListItemButton";
import ListItemIcon from "@mui/material/ListItemIcon";
import ListItemText from "@mui/material/ListItemText";
import ListSubheader from "@mui/material/ListSubheader";
import Menu from "@mui/material/Menu";
import MenuItem from "@mui/material/MenuItem";
import Toolbar from "@mui/material/Toolbar";
import Tooltip from "@mui/material/Tooltip";
import Typography from "@mui/material/Typography";
import { useColorScheme } from "@mui/material/styles";
import AccountCircle from "@mui/icons-material/AccountCircle";
import MenuIcon from "@mui/icons-material/Menu";
import MenuOpen from "@mui/icons-material/MenuOpen";
import { useTranslation } from "react-i18next";
import {
  atLeast,
  canAccess,
  roles as roleOrder,
  useSession,
  useTenant,
  useTenantSettings,
  type ModuleManifest,
  type Role,
} from "@farm360/web-core";
import { Icon, tokens } from "@farm360/ui-kit";

interface Props {
  manifests: readonly ModuleManifest[];
}

const collapsedKey = "farm360.sidebar.collapsed";

/** The sidebar's width choice is a per-person convenience: remembered when the browser allows it, never required. */
function readCollapsed(): boolean {
  try {
    return window.localStorage.getItem(collapsedKey) === "1";
  } catch {
    return false;
  }
}

function writeCollapsed(value: boolean): void {
  try {
    window.localStorage.setItem(collapsedKey, value ? "1" : "0");
  } catch {
    // Not remembered; the sidebar still works.
  }
}

interface Entry {
  manifest: ModuleManifest;
  to: string;
  icon: string;
  label: string;
}

/**
 * The top bar, the sidebar and the page area. Menu entries come from the loaded modules and are shown by role (the API still
 * decides access). The sidebar collapses to icons on a wide screen and becomes a drawer on a narrow one.
 */
export function AppShell({ manifests }: Props) {
  const { t } = useTranslation();
  const session = useSession();
  const tenant = useTenant();
  const { branding } = useTenantSettings();
  const { pathname } = useLocation();
  const [mobileOpen, setMobileOpen] = useState(false);
  const [collapsed, setCollapsed] = useState(readCollapsed);
  const moduleRoles = session?.moduleRoles ?? {};

  const entries: Entry[] = manifests.flatMap((manifest) =>
    manifest.menu
      .filter((item) => canAccess(moduleRoles, manifest.id, item.requiredRole))
      .map((item) => ({
        manifest,
        icon: item.icon,
        to: `${manifest.basePath}${item.path ? `/${item.path}` : ""}`,
        label: t(item.label, { ns: manifest.i18n.namespace }),
      })),
  );
  const current = manifests.find((m) => pathname === m.basePath || pathname.startsWith(`${m.basePath}/`));
  const currentLabel = current ? t(current.menu[0]?.label ?? current.id, { ns: current.i18n.namespace }) : undefined;

  const width = collapsed ? tokens.layout.sidebarCollapsedWidth : tokens.layout.sidebarWidth;

  function toggleCollapsed() {
    setCollapsed((value) => {
      writeCollapsed(!value);
      return !value;
    });
  }

  return (
    <Box sx={{ display: "flex", minHeight: "100vh" }}>
      <Box
        component="a"
        href="#main"
        sx={{
          position: "absolute",
          left: -9999,
          "&:focus": { left: 8, top: 8, zIndex: (theme) => theme.zIndex.tooltip, bgcolor: "background.paper", p: 1 },
        }}
      >
        {t("common:app.skip")}
      </Box>

      <AppBar position="fixed" sx={{ zIndex: (theme) => theme.zIndex.drawer + 1 }}>
        <Toolbar sx={{ gap: 1, minHeight: tokens.layout.appBarHeight }}>
          <IconButton
            edge="start"
            aria-label={t("common:nav.openMenu")}
            onClick={() => setMobileOpen(true)}
            sx={{ display: { md: "none" } }}
          >
            <MenuIcon />
          </IconButton>
          <Tooltip title={collapsed ? t("common:nav.expand") : t("common:nav.collapse")}>
            <IconButton
              edge="start"
              aria-label={collapsed ? t("common:nav.expand") : t("common:nav.collapse")}
              aria-pressed={collapsed}
              onClick={toggleCollapsed}
              sx={{ display: { xs: "none", md: "inline-flex" } }}
            >
              {collapsed ? <MenuIcon /> : <MenuOpen />}
            </IconButton>
          </Tooltip>
          <Box
            component={RouterLink}
            to="/"
            sx={{ display: "flex", alignItems: "center", gap: 1, color: "inherit", textDecoration: "none", flexGrow: 1, minWidth: 0 }}
          >
            {branding.logoUrl ? <Box component="img" src={branding.logoUrl} alt="" sx={{ height: 32 }} /> : null}
            <Typography variant="h6" component="span" noWrap>
              {tenant.name || t("common:app.name")}
            </Typography>
          </Box>
          <UserMenu highestRole={highestRole(moduleRoles)} tenantName={tenant.name} />
        </Toolbar>
      </AppBar>

      <Drawer
        variant="permanent"
        open
        sx={{
          display: { xs: "none", md: "block" },
          width,
          flexShrink: 0,
          transition: (theme) => theme.transitions.create("width"),
          "& .MuiDrawer-paper": {
            width,
            boxSizing: "border-box",
            overflowX: "hidden",
            transition: (theme) => theme.transitions.create("width"),
          },
        }}
      >
        <Sidebar entries={entries} collapsed={collapsed} onNavigate={() => undefined} />
      </Drawer>
      <Drawer
        variant="temporary"
        open={mobileOpen}
        onClose={() => setMobileOpen(false)}
        ModalProps={{ keepMounted: true }}
        sx={{ display: { xs: "block", md: "none" }, "& .MuiDrawer-paper": { width: tokens.layout.sidebarWidth } }}
      >
        <Sidebar entries={entries} collapsed={false} onNavigate={() => setMobileOpen(false)} />
      </Drawer>

      <Box
        component="main"
        id="main"
        tabIndex={-1}
        sx={{
          flexGrow: 1,
          minWidth: 0,
          width: "100%",
          maxWidth: tokens.layout.contentMaxWidth,
          mt: `${tokens.layout.appBarHeight}px`,
          px: { xs: 2, md: 3 },
          py: 3,
        }}
      >
        {currentLabel ? (
          <Breadcrumbs aria-label={t("common:nav.breadcrumbs")} sx={{ mb: 2 }}>
            <Link component={RouterLink} to="/" color="inherit" underline="hover">
              {t("common:nav.home")}
            </Link>
            <Typography color="text.primary" aria-current="page">
              {currentLabel}
            </Typography>
          </Breadcrumbs>
        ) : null}
        <Outlet />
      </Box>
    </Box>
  );
}

function highestRole(moduleRoles: Partial<Record<string, Role>>): Role | null {
  let best: Role | null = null;
  for (const role of Object.values(moduleRoles)) {
    if (role && (best === null || atLeast(role, best))) best = role;
  }
  return best;
}

function Sidebar({ entries, collapsed, onNavigate }: { entries: Entry[]; collapsed: boolean; onNavigate: () => void }) {
  const { t } = useTranslation();
  return (
    <Box component="nav" aria-label={t("common:nav.menu")}>
      <Toolbar sx={{ minHeight: tokens.layout.appBarHeight }} />
      <List
        subheader={
          collapsed ? undefined : (
            <ListSubheader disableSticky sx={{ bgcolor: "transparent", lineHeight: "32px" }}>
              {t("common:nav.workspace")}
            </ListSubheader>
          )
        }
      >
        <NavItem to="/" end icon="Dashboard" label={t("common:nav.home")} collapsed={collapsed} onNavigate={onNavigate} />
      </List>
      <Divider />
      <List
        subheader={
          collapsed ? undefined : (
            <ListSubheader disableSticky sx={{ bgcolor: "transparent", lineHeight: "32px" }}>
              {t("common:nav.applications")}
            </ListSubheader>
          )
        }
      >
        {entries.map((entry) => (
          <NavItem key={entry.to} to={entry.to} icon={entry.icon} label={entry.label} collapsed={collapsed} onNavigate={onNavigate} />
        ))}
      </List>
    </Box>
  );
}

function NavItem({
  to,
  icon,
  label,
  collapsed,
  onNavigate,
  end,
}: {
  to: string;
  icon: string;
  label: string;
  collapsed: boolean;
  onNavigate: () => void;
  end?: boolean;
}) {
  const button = (
    <ListItemButton
      component={NavLink}
      to={to}
      end={end}
      onClick={onNavigate}
      aria-label={collapsed ? label : undefined}
      sx={{
        mx: 1,
        borderRadius: 1,
        justifyContent: collapsed ? "center" : "flex-start",
        "&.active": {
          backgroundColor: "action.selected",
          "& .MuiListItemText-primary": { fontWeight: tokens.font.weight.semibold },
        },
      }}
    >
      <ListItemIcon sx={{ minWidth: collapsed ? 0 : 40, justifyContent: "center" }}>
        <Icon name={icon} />
      </ListItemIcon>
      {collapsed ? null : <ListItemText primary={label} />}
    </ListItemButton>
  );
  return (
    <ListItem disablePadding>
      {collapsed ? (
        <Tooltip title={label} placement="right">
          {button}
        </Tooltip>
      ) : (
        button
      )}
    </ListItem>
  );
}

/** Who is signed in, the appearance choice and sign out. The session carries a role and a tenant, not a display name. */
function UserMenu({ highestRole: role, tenantName }: { highestRole: Role | null; tenantName: string }) {
  const { t } = useTranslation();
  const { mode, setMode } = useColorScheme();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const close = () => setAnchor(null);
  const known = (roleOrder as readonly string[]).includes(role ?? "");

  return (
    <>
      <Tooltip title={t("common:user.menu")}>
        <IconButton
          aria-label={t("common:user.menu")}
          aria-haspopup="menu"
          aria-expanded={anchor ? true : undefined}
          onClick={(e) => setAnchor(e.currentTarget)}
        >
          <AccountCircle />
        </IconButton>
      </Tooltip>
      <Menu anchorEl={anchor} open={Boolean(anchor)} onClose={close} slotProps={{ list: { dense: true } }}>
        <Box sx={{ px: 2, py: 1, maxWidth: 260 }}>
          {tenantName ? (
            <Typography variant="h5" component="p" noWrap>
              {tenantName}
            </Typography>
          ) : null}
          {role && known ? (
            <Typography variant="body2" color="text.secondary">
              {t("common:user.role", { role: t(`common:roles.${role}`) })}
            </Typography>
          ) : null}
        </Box>
        <Divider />
        <ListSubheader disableSticky sx={{ bgcolor: "transparent", lineHeight: "32px" }}>
          {t("common:user.appearance")}
        </ListSubheader>
        {(["light", "dark", "system"] as const).map((choice) => (
          <MenuItem key={choice} selected={mode === choice} onClick={() => setMode(choice)}>
            {t(`common:user.${choice}`)}
          </MenuItem>
        ))}
        <Divider />
        <MenuItem component="a" href="/bff/logout" onClick={signOut}>
          {t("common:actions.signOut")}
        </MenuItem>
      </Menu>
    </>
  );
}

/** Signing out is a POST (with the CSRF header), not a link, so a page can't sign you out by embedding an image. */
function signOut(event: React.MouseEvent) {
  event.preventDefault();
  void fetch("/bff/logout", { method: "POST", credentials: "same-origin", headers: { "X-CSRF": "1" }, redirect: "manual" }).finally(() =>
    window.location.assign("/"),
  );
}
