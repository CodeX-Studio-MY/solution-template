import { Component, Suspense, type ErrorInfo, type ReactNode } from "react";
import Button from "@mui/material/Button";
import CircularProgress from "@mui/material/CircularProgress";
import Stack from "@mui/material/Stack";
import { useTranslation } from "react-i18next";
import { EmptyState } from "@saaskit/ui-kit";

function ModuleError({ onRetry }: { onRetry: () => void }) {
  const { t } = useTranslation("common");
  return (
    <EmptyState
      title={t("errors.moduleFailed")}
      hint={t("errors.moduleFailedHint")}
      action={<Button onClick={onRetry}>{t("actions.retry")}</Button>}
    />
  );
}

interface Props {
  moduleId: string;
  children: ReactNode;
}

class ErrorBoundary extends Component<Props, { failed: boolean }> {
  override state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  override componentDidCatch(error: Error, info: ErrorInfo) {
    console.error(`Module "${this.props.moduleId}" failed`, error, info.componentStack);
  }

  override render() {
    return this.state.failed ? <ModuleError onRetry={() => this.setState({ failed: false })} /> : this.props.children;
  }
}

function Loading() {
  const { t } = useTranslation("common");
  return (
    <Stack role="status" aria-live="polite" sx={{ alignItems: "center", py: 6 }}>
      <CircularProgress aria-label={t("app.loadingModule")} />
    </Stack>
  );
}

/** Every module mounts inside its own error boundary and Suspense: one failing module never breaks the app. */
export function ModuleBoundary({ moduleId, children }: Props) {
  return (
    <ErrorBoundary moduleId={moduleId}>
      <Suspense fallback={<Loading />}>{children}</Suspense>
    </ErrorBoundary>
  );
}
