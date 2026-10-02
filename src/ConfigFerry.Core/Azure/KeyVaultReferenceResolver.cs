using System.Collections.Concurrent;
using ConfigFerry.Core.Abstractions;
using ConfigFerry.Core.Models;

namespace ConfigFerry.Core.Azure;

/// <summary>
/// Replaces <c>@Microsoft.KeyVault(...)</c> values with the actual secret using the signed-in user's identity.
/// A reference that cannot be resolved (no permission, secret missing, malformed) is kept verbatim and reported
/// as a warning, so one inaccessible secret does not fail the whole run.
/// </summary>
public sealed class KeyVaultReferenceResolver(ISecretReader secretReader) : IKeyVaultReferenceResolver
{
    private const int MaxParallelism = 8;

    public async Task<KeyVaultResolutionResult> ResolveAsync(
        AzureAppConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var warnings = new ConcurrentBag<string>();
        var parsed = new Dictionary<string, KeyVaultReference>(StringComparer.Ordinal);

        foreach (var value in configuration.AppSettings.Values.Concat(configuration.ConnectionStrings.Values))
        {
            if (!KeyVaultReference.IsReference(value) || parsed.ContainsKey(value))
            {
                continue;
            }

            if (KeyVaultReference.TryParse(value, out var reference))
            {
                parsed[value] = reference!;
            }
        }

        // Resolve each distinct reference once.
        var secrets = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        await Parallel.ForEachAsync(
            parsed,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelism, CancellationToken = cancellationToken },
            async (entry, ct) =>
            {
                var reference = entry.Value;
                try
                {
                    var secret = await secretReader
                        .GetSecretAsync(reference.VaultUri, reference.SecretName, reference.Version, ct);
                    secrets[entry.Key] = secret;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    warnings.Add($"Could not read Key Vault secret '{reference.SecretName}' from {reference.VaultUri.Host}: {Describe(ex)}");
                }
            });

        var resolved = 0;
        var settings = Resolve(configuration.AppSettings, secrets, warnings, ref resolved);
        var connectionStrings = Resolve(configuration.ConnectionStrings, secrets, warnings, ref resolved);

        return new KeyVaultResolutionResult(
            new AzureAppConfiguration(settings, connectionStrings),
            resolved,
            [.. warnings.Order(StringComparer.Ordinal)]);
    }

    private static Dictionary<string, string> Resolve(
        IReadOnlyDictionary<string, string> source,
        ConcurrentDictionary<string, string> secrets,
        ConcurrentBag<string> warnings,
        ref int resolved)
    {
        var result = new Dictionary<string, string>(source.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in source)
        {
            if (secrets.TryGetValue(value, out var secret))
            {
                result[key] = secret;
                resolved++;
            }
            else
            {
                if (KeyVaultReference.IsReference(value) && !KeyVaultReference.TryParse(value, out _))
                {
                    warnings.Add($"Setting '{key}' has a Key Vault reference in an unsupported format and was kept as is.");
                }

                result[key] = value;
            }
        }

        return result;
    }

    private static string Describe(Exception ex) =>
        ex is global::Azure.RequestFailedException { Status: 403 }
            ? "access denied (the signed-in user needs 'get' permission on secrets)."
            : ex.Message;
}
