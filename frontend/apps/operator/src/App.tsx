import { createBrowserRouter, Link as RouterLink, Outlet, RouterProvider } from "react-router";
import AppBar from "@mui/material/AppBar";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import CircularProgress from "@mui/material/CircularProgress";
import CssBaseline from "@mui/material/CssBaseline";
import Toolbar from "@mui/material/Toolbar";
import Typography from "@mui/material/Typography";
import { ThemeProvider } from "@mui/material/styles";
import { CacheProvider } from "@emotion/react";
import { useTranslation } from "react-i18next";
import { PlatformQueryProvider, unwrap, useApiQuery } from "@saaskit/web-core";
import { createTenantTheme, FeedbackHost, ProblemAlert, tokens } from "@saaskit/ui-kit";
import { emotionCache } from "./emotionCache";
import { tenancy } from "./api";
import { signOut, useOperatorSession } from "./session";
import { SignIn } from "./pages/SignIn";

const theme = createTenantTheme({});

const router = createBrowserRouter([
  {
    path: "/",
    element: <Layout />,
    children: [
      { index: true, lazy: () => import("./pages/Tenants") },
      { path: "tenants/:id", lazy: () => import("./pages/Tenant") },
    ],
  },
]);

export function App() {
  return (
    <CacheProvider value={emotionCache}>
      <ThemeProvider theme={theme} defaultMode="light">
        <CssBaseline />
        <PlatformQueryProvider>
          <Gate />
          <FeedbackHost />
        </PlatformQueryProvider>
      </ThemeProvider>
    </CacheProvider>
  );
}

/** Who is signed in decides what renders: a 401 from any call turns the console into the sign-in form. */
function Gate() {
  const status = useOperatorSession((s) => s.status);
  const me = useApiQuery(["tenancy", "operator", "me"], (signal) => unwrap(tenancy.GET("/api/tenancy/v1/operator/me", { signal })), {
    enabled: status !== "signedOut",
  });
  if (status === "signedOut") return <SignIn />;
  if (me.error) return <ProblemAlert error={me.error} />;
  if (status === "unknown" && !me.data) return <CircularProgress aria-label="Loading" sx={{ m: 4 }} />;
  return <RouterProvider router={router} />;
}

function Layout() {
  const { t } = useTranslation("operator");
  const me = useApiQuery(["tenancy", "operator", "me"], (signal) => unwrap(tenancy.GET("/api/tenancy/v1/operator/me", { signal })));
  return (
    <Box sx={{ minHeight: "100vh" }}>
      <AppBar position="static">
        <Toolbar sx={{ gap: 2, minHeight: tokens.layout.appBarHeight }}>
          <Typography variant="h6" component={RouterLink} to="/" sx={{ flexGrow: 1, color: "inherit", textDecoration: "none" }}>
            {t("title")}
          </Typography>
          {me.data ? <Typography variant="body2">{me.data.displayName}</Typography> : null}
          <Button color="inherit" onClick={() => void signOut()}>
            {t("signOut")}
          </Button>
        </Toolbar>
      </AppBar>
      <Box component="main" id="main" sx={{ maxWidth: tokens.layout.contentMaxWidth, mx: "auto", px: { xs: 2, md: 3 }, py: 3 }}>
        <Outlet />
      </Box>
    </Box>
  );
}
