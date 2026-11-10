import { useEffect, useRef } from "react";
import { createBrowserRouter, Link as RouterLink, NavLink, Outlet, RouterProvider } from "react-router";
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
import { getI18n, PlatformQueryProvider, setLanguage, unwrap, useApiQuery } from "@saaskit/web-core";
import { createTenantTheme, FeedbackHost, LanguageMenu, ProblemAlert, tokens } from "@saaskit/ui-kit";
import { emotionCache } from "./emotionCache";
import { tenancy } from "./api";
import { signOut, useOperatorSession } from "./session";
import { SignIn } from "./pages/SignIn";

const { operator } = tokens;
const theme = createTenantTheme(
  { primaryColor: operator.primary },
  { background: operator.background, text: operator.text, divider: operator.divider, fontFamily: operator.fontFamily },
);

const router = createBrowserRouter([
  {
    path: "/",
    element: <Layout />,
    children: [
      { index: true, lazy: () => import("./pages/Tenants") },
      { path: "tenants/:id", lazy: () => import("./pages/Tenant") },
      { path: "principals", lazy: () => import("./pages/Principals") },
      { path: "principals/:id", lazy: () => import("./pages/Principal") },
      { path: "feedback", lazy: () => import("./pages/Feedback") },
      { path: "subscriptions", lazy: () => import("./pages/Subscriptions") },
      { path: "sign-in-providers", lazy: () => import("./pages/SignInProviders") },
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
  // The operator's saved language replaces the browser's, once, when the session is known.
  const applied = useRef(false);
  useEffect(() => {
    if (status === "signedOut") applied.current = false;
    else if (me.data && !applied.current) {
      applied.current = true;
      if (me.data.locale && me.data.locale !== getI18n().language) void setLanguage(me.data.locale);
    }
  }, [status, me.data]);
  if (status === "signedOut") return <SignIn />;
  if (me.error) return <ProblemAlert error={me.error} />;
  if (status === "unknown" && !me.data) return <CircularProgress aria-label="Loading" sx={{ m: 4 }} />;
  return <RouterProvider router={router} />;
}

/** Saves the operator's language. A failure keeps the language for this session and the menu says it was not saved. */
async function saveLanguage(locale: string): Promise<void> {
  await unwrap(tenancy.PUT("/api/tenancy/v1/operator/me/preferences", { body: { locale } }));
}

function Layout() {
  const { t } = useTranslation("operator");
  const me = useApiQuery(["tenancy", "operator", "me"], (signal) => unwrap(tenancy.GET("/api/tenancy/v1/operator/me", { signal })));
  return (
    <Box sx={{ minHeight: "100vh" }}>
      <AppBar position="static" sx={{ bgcolor: operator.bar, color: operator.onBar }}>
        <Toolbar sx={{ gap: 2, flexWrap: "wrap", minHeight: tokens.layout.appBarHeight }}>
          <Typography variant="h6" component={RouterLink} to="/" sx={{ color: "inherit", textDecoration: "none" }}>
            {t("title")}
          </Typography>
          <Box component="nav" aria-label={t("nav.label")} sx={{ display: "flex", flexWrap: "wrap", gap: 0.5, flexGrow: 1 }}>
            {(
              [
                ["/", "nav.tenants"],
                ["/principals", "nav.principals"],
                ["/feedback", "nav.feedback"],
                ["/subscriptions", "nav.subscriptions"],
                ["/sign-in-providers", "nav.signInProviders"],
              ] as const
            ).map(([to, label]) => (
              <Button
                key={to}
                component={NavLink}
                to={to}
                end={to === "/"}
                color="inherit"
                sx={{
                  textTransform: "none",
                  borderRadius: 0,
                  borderBottom: 2,
                  borderColor: "transparent",
                  "&.active": { borderColor: operator.onBar },
                }}
              >
                {t(label)}
              </Button>
            ))}
          </Box>
          <LanguageMenu onChange={saveLanguage} />
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
