import Box from "@mui/material/Box";
import Typography from "@mui/material/Typography";
import { useTranslation } from "react-i18next";
import { useFormatters } from "@saaskit/web-core";
import type { AccessAuditRow, AccessStatus } from "../api";

const stateColor: Record<AccessStatus, string> = { active: "success.main", suspended: "warning.main", revoked: "error.main" };

/**
 * One principal's access history in one tenant, newest first. The line reads as a state over time: solid while access was active,
 * dashed while suspended, and absent while revoked, with a square stop where it ended. The text says the same, so nothing relies on colour.
 */
export function AccessTrail({ rows }: { rows: AccessAuditRow[] }) {
  const { t } = useTranslation("operator");
  const format = useFormatters();
  return (
    <Box component="ol" sx={{ listStyle: "none", m: 0, p: 0 }}>
      {rows.map((row, index) => {
        // The line below an entry is the state that lasted until that entry: what the older entry left behind.
        const before = rows[index + 1]?.toStatus;
        return (
          <Box component="li" key={row.id} sx={{ display: "grid", gridTemplateColumns: "16px minmax(0, 1fr)", columnGap: 1.5 }}>
            <Box aria-hidden sx={{ display: "flex", flexDirection: "column", alignItems: "center" }}>
              <Box
                data-state={row.toStatus}
                sx={{
                  flexShrink: 0,
                  mt: "5px",
                  width: row.toStatus === "revoked" ? 14 : 10,
                  height: row.toStatus === "revoked" ? 4 : 10,
                  mb: row.toStatus === "revoked" ? "6px" : 0,
                  borderRadius: row.toStatus === "revoked" ? 0 : "50%",
                  border: 2,
                  borderColor: stateColor[row.toStatus],
                  bgcolor: row.toStatus === "suspended" ? "background.paper" : stateColor[row.toStatus],
                }}
              />
              {before && before !== "revoked" ? (
                <Box
                  data-line={before}
                  sx={{
                    flexGrow: 1,
                    width: 0,
                    borderLeft: 2,
                    borderLeftStyle: before === "suspended" ? "dashed" : "solid",
                    borderColor: stateColor[before],
                  }}
                />
              ) : null}
            </Box>
            <Box sx={{ pb: 2.5, minWidth: 0 }}>
              <Typography variant="body2" sx={{ fontWeight: "fontWeightMedium" }}>
                {t(`access.actions.${row.action}`)}
              </Typography>
              <Typography variant="caption" color="text.secondary" component="p">
                {format.dateTime(row.occurredOn)}, {row.actorName ?? row.actorId}
              </Typography>
              {row.reason ? (
                <Typography variant="body2" sx={{ mt: 0.5, overflowWrap: "anywhere" }}>
                  {row.reason}
                </Typography>
              ) : null}
            </Box>
          </Box>
        );
      })}
    </Box>
  );
}
