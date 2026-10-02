using ConfigFerry.Core.Abstractions;
using ConfigFerry.Core.Azure;
using ConfigFerry.Core.Configuration;
using ConfigFerry.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ConfigFerry.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything in ConfigFerry.Core against its interface. Consumers (view model, resolver, ...) only ever
    /// receive interfaces, so every class that talks to Azure or the file system can be substituted in tests.
    /// The host must still register the platform services <see cref="IFilePickerService"/> and
    /// <see cref="IClipboardService"/> (and a logger factory).
    /// </summary>
    public static IServiceCollection AddConfigFerryCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Azure access
        services.AddSingleton<IAuthSessionFactory, EntraAuthSessionFactory>();
        services.AddSingleton<IAzureAuthService, AzureAuthService>();
        services.AddSingleton<IArmClientFactory, ArmClientFactory>();
        services.AddSingleton<ISecretClientFactory, SecretClientFactory>();
        services.AddSingleton<IAzureResourceService, AzureResourceService>();
        services.AddSingleton<ISecretReader, KeyVaultSecretReader>();
        services.AddSingleton<IKeyVaultReferenceResolver, KeyVaultReferenceResolver>();

        // Local logic and IO
        services.AddSingleton<IConfigGenerator, ConfigGenerator>();
        services.AddSingleton<ITextFileStore, TextFileStore>();

        services.AddSingleton<MainViewModel>();
        return services;
    }
}
