import { useFormatters } from "@acme/web-core";

interface MoneyTextProps {
  amount: number | string;
  /** Defaults to the tenant's currency. */
  currency?: string;
}

/** An amount in the tenant's locale. */
export function MoneyText({ amount, currency }: MoneyTextProps) {
  const format = useFormatters();
  return <span>{format.money(amount, currency)}</span>;
}
