using System.Globalization;

namespace Farm360.Core.Data;

/// <summary>An amount with its ISO 4217 currency. Stored as decimal(19,4) / numeric(19,4); never float or double.</summary>
public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money Zero(string currency) => new(0m, currency);

    public static Money operator +(Money left, Money right) => new(left.Amount + right.Amount, SameCurrency(left, right));

    public static Money operator -(Money left, Money right) => new(left.Amount - right.Amount, SameCurrency(left, right));

    public static Money operator *(Money money, decimal factor) => new(money.Amount * factor, money.Currency);

    public static bool operator <(Money left, Money right) => Compare(left, right) < 0;

    public static bool operator >(Money left, Money right) => Compare(left, right) > 0;

    public static bool operator <=(Money left, Money right) => Compare(left, right) <= 0;

    public static bool operator >=(Money left, Money right) => Compare(left, right) >= 0;

    public Money Add(Money other) => this + other;

    public Money Subtract(Money other) => this - other;

    public Money Multiply(decimal factor) => this * factor;

    public int CompareTo(Money other) => Compare(this, other);

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Currency} {Amount:0.00##}");

    private static int Compare(Money left, Money right)
    {
        SameCurrency(left, right);
        return left.Amount.CompareTo(right.Amount);
    }

    private static string SameCurrency(Money left, Money right) =>
        string.Equals(left.Currency, right.Currency, StringComparison.Ordinal)
            ? left.Currency
            : throw new InvalidOperationException($"Cannot combine {left.Currency} and {right.Currency}.");
}
