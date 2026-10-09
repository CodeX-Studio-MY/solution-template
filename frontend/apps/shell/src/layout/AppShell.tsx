import { useState } from "react";
import { Link as RouterLink, NavLink, Outlet } from "react-router";
import AppBar from "@mui/material/AppBar";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Drawer from "@mui/material/Drawer";
import IconButton from "@mui/material/IconButton";
import List from "@mui/material/List";
import ListItem from "@mui/material/ListItem";
import ListItemButton from "@mui/material/ListItemButton";
import ListItemIcon from "@mui/material/ListItemIcon";
import ListItemText from "@mui/material/ListItemText";
import Toolbar from "@mui/material/Toolbar";
import Typography from "@mui/material/Typography";
import MenuIcon from "@mui/icons-material/Menu";
import { useTranslation } from "react-i18next";
import { canAccess, useSession, useTenant, useTenantSettings, type ModuleManifest } from "@acme/web-core";
import { Icon, tokens } from "@acme/ui-kit";

interface Props {
  manifests: readonly ModuleManifest[];
}

/** The navbar and sidebar. Menu entries come from the loaded modules and are shown by role (the API still decides access). */
export function AppShell({ manifests }: Props) {
  const { t } = useTranslation();
  const session = useSession();
  const tenant = useTenant();
  const { branding } = useTenantSettings();
  const [open, setOpen] = useState(false);
  const moduleRoles = session?.moduleRoles ?? {};

  const items = manifests.flatMap((manifest) =>
    manifest.menu
      .filter((item) => canAccess(moduleRoles, manifest.id, item.requiredRole))
      .map((item) => ({ manifest, item, to: `${manifest.basePath}${item.path ? `/${item.path}` : ""}` })),
  );

  const drawer = (
    <Box component="nav" aria-label={t("common:nav.menu")} sx={{ width: tokens.layout.sidebarWidth }}>
      <Toolbar />
      <List>
        <ListItem disablePadding>
          <ListItemButton component={NavLink} to="/" end onClick={() => setOpen(false)}>
            <ListItemIcon>
              <Icon name="Dashboard" />
            </ListItemIcon>
            <ListItemText primary={t("common:nav.home")} />
          </ListItemButton>
        </ListItem>
        {items.map(({ manifest, item, to }) => (
          <ListItem key={to} disablePadding>
            <ListItemButton component={NavLink} to={to} onClick={() => setOpen(false)}>
              <ListItemIcon>
                <Icon name={item.icon} />
              </ListItemIcon>
              <ListItemText primary={t(item.label, { ns: manifest.i18n.namespace })} />
            </ListItemButton>
          </ListItem>
        ))}
      </List>
    </Box>
  );

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
        <Toolbar>
          <IconButton
            color="inherit"
            edge="start"
            aria-label={t("common:nav.openMenu")}
            onClick={() => setOpen(true)}
            sx={{ mr: 1, display: { md: "none" } }}
          >
            <MenuIcon />
          </IconButton>
          <Box
            component={RouterLink}
            to="/"
            sx={{ display: "flex", alignItems: "center", gap: 1, color: "inherit", textDecoration: "none", flexGrow: 1 }}
          >
            {branding.logoUrl ? <Box component="img" src={branding.logoUrl} alt="" sx={{ height: 32 }} /> : null}
            <Typography variant="h6" component="span">
              {tenant.name || t("common:app.name")}
            </Typography>
          </Box>
          <Button color="inherit" component="a" href="/bff/logout" onClick={signOut}>
            {t("common:actions.signOut")}
          </Button>
        </Toolbar>
      </AppBar>
      <Drawer
        variant="permanent"
        sx={{
          display: { xs: "none", md: "block" },
          width: tokens.layout.sidebarWidth,
          flexShrink: 0,
          "& .MuiDrawer-paper": { width: tokens.layout.sidebarWidth, boxSizing: "border-box" },
        }}
        open
      >
        {drawer}
      </Drawer>
      <Drawer
        variant="temporary"
        open={open}
        onClose={() => setOpen(false)}
        ModalProps={{ keepMounted: true }}
        sx={{ display: { xs: "block", md: "none" } }}
      >
        {drawer}
      </Drawer>
      <Box
        component="main"
        id="main"
        tabIndex={-1}
        sx={{ flexGrow: 1, p: 3, maxWidth: tokens.layout.contentMaxWidth, mt: `${tokens.layout.appBarHeight}px` }}
      >
        <Outlet />
      </Box>
    </Box>
  );
}

/** Signing out is a POST (with the CSRF header), not a link, so a page can't sign you out by embedding an image. */
function signOut(event: React.MouseEvent) {
  event.preventDefault();
  void fetch("/bff/logout", { method: "POST", credentials: "same-origin", headers: { "X-CSRF": "1" }, redirect: "manual" }).finally(() =>
    window.location.assign("/"),
  );
}
