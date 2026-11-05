import { useState } from "react";
import IconButton from "@mui/material/IconButton";
import ListItemText from "@mui/material/ListItemText";
import Menu from "@mui/material/Menu";
import MenuItem from "@mui/material/MenuItem";
import Tooltip from "@mui/material/Tooltip";
import LanguageIcon from "@mui/icons-material/Language";
import { useTranslation } from "react-i18next";
import { platformDefaults, setLanguage, snackbar } from "@saaskit/web-core";

/** A language's own name: "English", "Bahasa Melayu", "中文". The browser knows them, so a new language needs no list. */
export function languageName(locale: string): string {
  return new Intl.DisplayNames([locale], { type: "language" }).of(locale) ?? locale;
}

/**
 * Switches the language at once (no reload). `onChange` is where the app saves the choice; if saving fails the language
 * stays for this session and a short message says it was not saved. Apps that don't save (a sign-in screen) pass nothing.
 */
export function LanguageMenu({
  onChange,
  locales = platformDefaults.locales,
}: {
  onChange?: (locale: string) => void | Promise<unknown>;
  locales?: readonly string[];
}) {
  const { t, i18n } = useTranslation();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const current = i18n.resolvedLanguage ?? i18n.language;

  async function choose(locale: string) {
    setAnchor(null);
    if (locale === current) return;
    await setLanguage(locale);
    if (!onChange) return;
    try {
      await onChange(locale);
    } catch {
      snackbar.warning(t("common:language.notSaved"));
    }
  }

  return (
    <>
      <Tooltip title={t("common:language.label")}>
        <IconButton
          color="inherit"
          aria-label={t("common:language.label")}
          aria-haspopup="menu"
          aria-expanded={anchor ? true : undefined}
          onClick={(e) => setAnchor(e.currentTarget)}
        >
          <LanguageIcon />
        </IconButton>
      </Tooltip>
      <Menu anchorEl={anchor} open={Boolean(anchor)} onClose={() => setAnchor(null)} slotProps={{ list: { dense: true } }}>
        {locales.map((locale) => (
          <MenuItem key={locale} selected={locale === current} lang={locale} onClick={() => void choose(locale)}>
            <ListItemText>{languageName(locale)}</ListItemText>
          </MenuItem>
        ))}
      </Menu>
    </>
  );
}
