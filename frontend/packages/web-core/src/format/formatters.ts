import { useMemo } from "react";
import { useTenant } from "../tenant/tenantStore";

/** An amount of money. The API sends decimals as numbers (or, rarely, strings). */
export interface Money {
  amount: number;
  currency: string;
}

export function toNumber(value: number | string): number {
  return typeof value === "number" ? value : Number(value);
}

export interface FormatContext {
  locale: string;
  timeZone: string;
  currency: string;
}

export function formatMoney(
  value: number | string,
  context: Pick<FormatContext, "locale" | "currency">,
  currency: string = context.currency,
): string {
  return new Intl.NumberFormat(context.locale, { style: "currency", currency }).format(toNumber(value));
}

export function formatNumber(value: number | string, context: Pick<FormatContext, "locale">, options?: Intl.NumberFormatOptions): string {
  return new Intl.NumberFormat(context.locale, options).format(toNumber(value));
}

/** A calendar date (`2026-10-09`) shown as-is in the locale: it has no time zone, so none is applied. */
export function formatDate(value: string, context: Pick<FormatContext, "locale">): string {
  const [year, month, day] = value.split("-").map(Number);
  return new Intl.DateTimeFormat(context.locale, { dateStyle: "medium", timeZone: "UTC" }).format(
    new Date(Date.UTC(year ?? 1970, (month ?? 1) - 1, day ?? 1)),
  );
}

/** A moment (an ISO date-time in UTC) shown in the tenant's time zone. */
export function formatDateTime(value: string, context: Pick<FormatContext, "locale" | "timeZone">): string {
  return new Intl.DateTimeFormat(context.locale, { dateStyle: "medium", timeStyle: "short", timeZone: context.timeZone }).format(
    new Date(value),
  );
}

/** Formatting in the tenant's own locale, time zone and currency. */
export function useFormatters() {
  const tenant = useTenant();
  return useMemo(() => {
    const context: FormatContext = { locale: tenant.locale, timeZone: tenant.timeZone, currency: tenant.currency };
    return {
      money: (value: number | string, currency?: string) => formatMoney(value, context, currency),
      number: (value: number | string, options?: Intl.NumberFormatOptions) => formatNumber(value, context, options),
      date: (value: string) => formatDate(value, context),
      dateTime: (value: string) => formatDateTime(value, context),
      context,
    };
  }, [tenant.locale, tenant.timeZone, tenant.currency]);
}
