import { useState } from "react";
import { useLocation } from "react-router";
import Rating from "@mui/material/Rating";
import TextField from "@mui/material/TextField";
import ToggleButton from "@mui/material/ToggleButton";
import ToggleButtonGroup from "@mui/material/ToggleButtonGroup";
import Typography from "@mui/material/Typography";
import { useTranslation } from "react-i18next";
import { ApiError, unwrap, useApiMutation } from "@saaskit/web-core";
import { FormDialog } from "@saaskit/ui-kit";
import { tenancy, tenancyQueries, type FeedbackKind } from "../settings/api";

const kinds: readonly FeedbackKind[] = ["bug", "idea", "other"];
const maxLength = 2000;

/** What anyone signed in can tell the provider: what kind it is, how it felt, the message, and the page it was sent from. */
export function FeedbackDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation();
  const { pathname } = useLocation();
  const [kind, setKind] = useState<FeedbackKind>("idea");
  const [rating, setRating] = useState<number | null>(null);
  const [message, setMessage] = useState("");
  const send = useApiMutation(
    () => unwrap(tenancy.POST("/api/tenancy/v1/feedback", { body: { kind, rating, message, pagePath: pathname } })),
    { invalidate: tenancyQueries, success: t("common:feedback.sent"), onSuccess: onClose },
  );
  const error = send.error instanceof ApiError ? send.error.forField("message")?.message : undefined;

  return (
    <FormDialog
      open
      title={t("common:feedback.title")}
      text={t("common:feedback.text")}
      submitLabel={t("common:feedback.send")}
      busy={send.isPending}
      onSubmit={() => send.mutate()}
      onClose={onClose}
    >
      <ToggleButtonGroup
        exclusive
        size="small"
        color="primary"
        aria-label={t("common:feedback.kind")}
        value={kind}
        onChange={(_, next: FeedbackKind | null) => next && setKind(next)}
        sx={{
          "& .MuiToggleButton-root": { color: "text.primary", textTransform: "none", flex: 1 },
          "& .MuiToggleButton-root.Mui-selected, & .MuiToggleButton-root.Mui-selected:hover": {
            bgcolor: "primary.main",
            color: "primary.contrastText",
          },
        }}
      >
        {kinds.map((value) => (
          <ToggleButton key={value} value={value}>
            {t(`common:feedback.kinds.${value}`)}
          </ToggleButton>
        ))}
      </ToggleButtonGroup>
      <div>
        <Typography component="legend" variant="body2" color="text.secondary">
          {t("common:feedback.rating")}
        </Typography>
        <Rating name="feedback-rating" value={rating} onChange={(_, next) => setRating(next)} />
      </div>
      <TextField
        label={t("common:feedback.message")}
        required
        autoFocus
        multiline
        minRows={4}
        value={message}
        onChange={(e) => setMessage(e.target.value)}
        error={Boolean(error)}
        helperText={error ?? `${message.length} / ${maxLength}`}
        slotProps={{ htmlInput: { maxLength } }}
      />
    </FormDialog>
  );
}
