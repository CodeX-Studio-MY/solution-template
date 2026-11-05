import { useState, type FormEvent } from "react";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Paper from "@mui/material/Paper";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { useTranslation } from "react-i18next";
import { LanguageMenu, ProblemAlert } from "@saaskit/ui-kit";
import { signIn } from "../session";

/** Email, password and a TOTP code: operators always use two factors. */
export function SignIn() {
  const { t } = useTranslation("operator");
  const [form, setForm] = useState({ email: "", password: "", totpCode: "" });
  const [error, setError] = useState<Error | undefined>(undefined);
  const [busy, setBusy] = useState(false);
  const set = (name: keyof typeof form) => (value: string) => setForm((f) => ({ ...f, [name]: value }));

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(undefined);
    try {
      await signIn(form);
    } catch (e) {
      setError(e as Error);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Stack component="main" sx={{ minHeight: "100vh", alignItems: "center", justifyContent: "center", p: 2, position: "relative" }}>
      {/* Changes the language of this screen only: nobody is signed in, so there is nothing to save it to. */}
      <Box sx={{ position: "absolute", top: 8, insetInlineEnd: 8 }}>
        <LanguageMenu />
      </Box>
      <Paper variant="outlined" sx={{ p: 4, width: "100%", maxWidth: 400 }}>
        <form onSubmit={submit} noValidate>
          <Stack spacing={2.5}>
            <Typography variant="h1" component="h1">
              {t("signIn.title")}
            </Typography>
            <Typography variant="body2" color="text.secondary">
              {t("signIn.subtitle")}
            </Typography>
            <ProblemAlert error={error} />
            <TextField
              label={t("signIn.email")}
              type="email"
              autoComplete="username"
              required
              autoFocus
              value={form.email}
              onChange={(e) => set("email")(e.target.value)}
            />
            <TextField
              label={t("signIn.password")}
              type="password"
              autoComplete="current-password"
              required
              value={form.password}
              onChange={(e) => set("password")(e.target.value)}
            />
            <TextField
              label={t("signIn.totp")}
              autoComplete="one-time-code"
              required
              value={form.totpCode}
              onChange={(e) => set("totpCode")(e.target.value.replace(/\D/g, "").slice(0, 6))}
              slotProps={{ htmlInput: { inputMode: "numeric", pattern: "[0-9]*" } }}
            />
            <Button type="submit" variant="contained" disabled={busy}>
              {t("signIn.submit")}
            </Button>
          </Stack>
        </form>
      </Paper>
    </Stack>
  );
}
