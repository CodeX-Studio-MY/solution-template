import Alert from "@mui/material/Alert";
import { useTranslation } from "react-i18next";
import { ApiError } from "@saaskit/web-core";

interface ProblemAlertProps {
  error: Error | undefined;
  /** Shown for errors that aren't from the API. */
  fallbackKey?: string;
}

/**
 * An error as a message people can act on. An API error shows its localized message (the server already answered in the
 * caller's language); field errors are shown by the form, not here.
 */
export function ProblemAlert({ error, fallbackKey = "common:errors.generic" }: ProblemAlertProps) {
  const { t } = useTranslation();
  if (!error) return null;
  const message = error instanceof ApiError ? error.message : t(fallbackKey);
  return (
    <Alert severity="error" role="alert">
      {message}
      {error instanceof ApiError && error.traceId ? <small> ({error.traceId})</small> : null}
    </Alert>
  );
}
