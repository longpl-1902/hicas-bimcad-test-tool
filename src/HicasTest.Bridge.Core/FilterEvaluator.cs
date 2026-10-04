using System;
using System.Globalization;
using HicasTest.Protocol;

namespace HicasTest.Bridge.Core
{
    /// <summary>Evaluates one <see cref="Filter"/> against a value read from the host. Pure; shared by both bridges.</summary>
    public static class FilterEvaluator
    {
        private const double DefaultTolerance = 1e-9;

        public static bool Matches(ParamValue value, Filter filter)
        {
            var op = (filter.Op ?? "eq").ToLowerInvariant();
            if (op == "exists")
                return value != null && value.Found;
            if (value == null || !value.Found)
                return false;

            if (value.Number.HasValue && TryParse(filter.Value, out var expected))
                return CompareNumber(value.Number.Value, expected, filter.Tolerance ?? DefaultTolerance, op);

            return CompareText(value.Text ?? value.Display ?? string.Empty, filter.Value ?? string.Empty, op);
        }

        public static bool TryParse(string text, out double number) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);

        private static bool CompareNumber(double actual, double expected, double tolerance, string op)
        {
            switch (op)
            {
                case "eq": return Math.Abs(actual - expected) <= tolerance;
                case "ne": return Math.Abs(actual - expected) > tolerance;
                case "gt": return actual > expected;
                case "ge": return actual >= expected - tolerance;
                case "lt": return actual < expected;
                case "le": return actual <= expected + tolerance;
                case "contains": return actual.ToString(CultureInfo.InvariantCulture).IndexOf(expected.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal) >= 0;
                default: throw new NotSupportedException("Unknown filter op '" + op + "'.");
            }
        }

        private static bool CompareText(string actual, string expected, string op)
        {
            switch (op)
            {
                case "eq": return string.Equals(actual, expected, StringComparison.Ordinal);
                case "ne": return !string.Equals(actual, expected, StringComparison.Ordinal);
                case "contains": return actual.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
                case "gt": return string.CompareOrdinal(actual, expected) > 0;
                case "ge": return string.CompareOrdinal(actual, expected) >= 0;
                case "lt": return string.CompareOrdinal(actual, expected) < 0;
                case "le": return string.CompareOrdinal(actual, expected) <= 0;
                default: throw new NotSupportedException("Unknown filter op '" + op + "'.");
            }
        }
    }
}
