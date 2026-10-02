using ConfigFerry.Core.Abstractions;
using ConfigFerry.Core.Azure;
using ConfigFerry.Core.Configuration;
using ConfigFerry.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace ConfigFerry.Core.Tests;

/// <summary>Guards the DI wiring: consumers must receive interfaces, and the whole graph must resolve.</summary>
[TestClass]
public class CompositionTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IFilePickerService>());
        services.AddSingleton(Substitute.For<IClipboardService>());
        services.AddConfigFerryCore();

        // ValidateOnBuild proves every registered service can actually be constructed from the container.
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [TestMethod]
    public void MainViewModel_CanBeResolved_WithAllDependencies()
    {
        using var provider = BuildProvider();

        Assert.IsNotNull(provider.GetRequiredService<MainViewModel>());
    }

    [TestMethod]
    [DataRow(typeof(IAuthSessionFactory), typeof(EntraAuthSessionFactory))]
    [DataRow(typeof(IAzureAuthService), typeof(AzureAuthService))]
    [DataRow(typeof(IArmClientFactory), typeof(ArmClientFactory))]
    [DataRow(typeof(ISecretClientFactory), typeof(SecretClientFactory))]
    [DataRow(typeof(IAzureResourceService), typeof(AzureResourceService))]
    [DataRow(typeof(ISecretReader), typeof(KeyVaultSecretReader))]
    [DataRow(typeof(IKeyVaultReferenceResolver), typeof(KeyVaultReferenceResolver))]
    [DataRow(typeof(IConfigGenerator), typeof(ConfigGenerator))]
    [DataRow(typeof(ITextFileStore), typeof(TextFileStore))]
    public void Interface_IsRegisteredWithItsImplementation(Type service, Type implementation)
    {
        using var provider = BuildProvider();

        Assert.IsInstanceOfType(provider.GetRequiredService(service), implementation);
    }

    [TestMethod]
    public void Services_AreSingletons_SoOneSessionIsShared()
    {
        using var provider = BuildProvider();

        Assert.AreSame(provider.GetRequiredService<IAzureAuthService>(), provider.GetRequiredService<IAzureAuthService>());
        Assert.AreSame(provider.GetRequiredService<MainViewModel>(), provider.GetRequiredService<MainViewModel>());
    }

    [TestMethod]
    public void NoCoreClass_DependsOnAConcreteServiceOfTheSameAssembly()
    {
        // Constructor parameters of Core services must be interfaces (or framework types), never our own classes.
        var assembly = typeof(MainViewModel).Assembly;
        var coreTypes = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true }
                && (t == typeof(MainViewModel) || t.GetInterfaces().Any(i => i.Assembly == assembly)));

        var violations = coreTypes
            .SelectMany(t => t.GetConstructors().SelectMany(c => c.GetParameters().Select(p => (Type: t, Param: p))))
            .Where(x => x.Param.ParameterType.Assembly == typeof(MainViewModel).Assembly && !x.Param.ParameterType.IsInterface)
            .Select(x => $"{x.Type.Name}({x.Param.ParameterType.Name} {x.Param.Name})")
            .ToArray();

        Assert.IsEmpty(violations, string.Join(", ", violations));
    }
}
