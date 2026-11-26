using System;

namespace RealizeJSON.Diagnostics;

/// <summary>
/// Heuristic detection of member names that commonly hold sensitive data. This is a hint, not a security assessment.
/// </summary>
public static class SensitiveNameHeuristic
{
    private static readonly string[] Fragments =
    {
        "password", "passwd", "pwd", "secret", "token", "apikey", "api_key", "privatekey", "private_key",
        "connectionstring", "credential", "ssn", "socialsecurity", "creditcard", "cardnumber", "cvv", "pin",
    };

    public static bool IsPotentiallySensitive(string memberName)
    {
        if (string.IsNullOrEmpty(memberName))
        {
            return false;
        }

        var normalized = memberName.Replace("-", string.Empty).Replace(" ", string.Empty);
        foreach (var fragment in Fragments)
        {
            if (fragment == "pin")
            {
                // Only whole-word "Pin" (avoid "Shipping", "Mapping").
                if (string.Equals(normalized, "pin", StringComparison.OrdinalIgnoreCase) || normalized.EndsWith("Pin", StringComparison.Ordinal))
                {
                    return true;
                }

                continue;
            }

            if (normalized.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
