using System.Text.RegularExpressions;

namespace ConfigFerry.Core.Azure;

/// <summary>
/// A parsed App Service Key Vault reference, e.g.
/// <c>@Microsoft.KeyVault(SecretUri=https://v.vault.azure.net/secrets/name/version)</c> or
/// <c>@Microsoft.KeyVault(VaultName=v;SecretName=name;SecretVersion=version)</c>.
/// </summary>
public sealed partial record KeyVaultReference(Uri VaultUri, string SecretName, string? Version)
{
    [GeneratedRegex(@"^@Microsoft\.KeyVault\((?<body>.+)\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex ReferencePattern();

    /// <summary>True when the value looks like a Key Vault reference (even a malformed one).</summary>
    public static bool IsReference(string? value) =>
        value is not null && ReferencePattern().IsMatch(value.Trim());

    public static bool TryParse(string? value, out KeyVaultReference? reference)
    {
        reference = null;
        if (value is null)
        {
            return false;
        }

        var match = ReferencePattern().Match(value.Trim());
        if (!match.Success)
        {
            return false;
        }

        var body = match.Groups["body"].Value;
        var parts = ParseParts(body);

        if (parts.TryGetValue("SecretUri", out var secretUri))
        {
            return TryFromSecretUri(secretUri, out reference);
        }

        if (parts.TryGetValue("VaultName", out var vaultName)
            && parts.TryGetValue("SecretName", out var secretName)
            && !string.IsNullOrWhiteSpace(vaultName)
            && !string.IsNullOrWhiteSpace(secretName)
            && Uri.TryCreate($"https://{vaultName}.vault.azure.net/", UriKind.Absolute, out var vaultUri))
        {
            parts.TryGetValue("SecretVersion", out var version);
            reference = new KeyVaultReference(vaultUri, secretName, string.IsNullOrWhiteSpace(version) ? null : version);
            return true;
        }

        return false;
    }

    private static bool TryFromSecretUri(string secretUri, out KeyVaultReference? reference)
    {
        reference = null;
        if (!Uri.TryCreate(secretUri, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        // Path: /secrets/{name}[/{version}]
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length is < 2 or > 3 || !segments[0].Equals("secrets", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var vaultUri = new Uri($"{uri.Scheme}://{uri.Authority}/");
        reference = new KeyVaultReference(vaultUri, segments[1], segments.Length == 3 ? segments[2] : null);
        return true;
    }

    private static Dictionary<string, string> ParseParts(string body)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in body.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0)
            {
                result[part[..separator].Trim()] = part[(separator + 1)..].Trim();
            }
        }

        return result;
    }
}
