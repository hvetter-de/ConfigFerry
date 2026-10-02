using ConfigFerry.Core.Abstractions;
using ConfigFerry.Core.Models;
using global::Azure.Core;
using global::Azure.ResourceManager;
using global::Azure.ResourceManager.AppService;
using global::Azure.ResourceManager.Resources;

namespace ConfigFerry.Core.Azure;

public sealed class AzureResourceService(IAzureAuthService auth) : IAzureResourceService
{
    public async Task<IReadOnlyList<TenantInfo>> GetTenantsAsync(CancellationToken cancellationToken)
    {
        var result = new List<TenantInfo>();
        await foreach (var tenant in CreateClient().GetTenants().GetAllAsync(cancellationToken))
        {
            if (tenant.Data.TenantId is not { } id)
            {
                continue;
            }

            var name = tenant.Data.DisplayName ?? tenant.Data.DefaultDomain ?? id.ToString();
            var label = tenant.Data.DefaultDomain is { } domain && domain != name ? $"{name} ({domain})" : name;
            result.Add(new TenantInfo(id.ToString(), label));
        }

        return [.. result.OrderBy(t => t.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
    }

    public async Task<IReadOnlyList<SubscriptionInfo>> GetSubscriptionsAsync(CancellationToken cancellationToken)
    {
        var result = new List<SubscriptionInfo>();
        await foreach (var subscription in CreateClient().GetSubscriptions().GetAllAsync(cancellationToken))
        {
            result.Add(new SubscriptionInfo(subscription.Data.SubscriptionId, subscription.Data.DisplayName));
        }

        return [.. result.OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
    }

    public async Task<IReadOnlyList<AppServiceInfo>> GetAppServicesAsync(
        string subscriptionId, CancellationToken cancellationToken)
    {
        var client = CreateClient();
        var subscription = client.GetSubscriptionResource(SubscriptionResource.CreateResourceIdentifier(subscriptionId));

        var result = new List<AppServiceInfo>();
        await foreach (var site in subscription.GetWebSitesAsync(cancellationToken))
        {
            var kind = site.Data.Kind?.Contains("functionapp", StringComparison.OrdinalIgnoreCase) == true
                ? AppServiceKind.FunctionApp
                : AppServiceKind.WebApp;
            result.Add(new AppServiceInfo(site.Id.ToString(), site.Data.Name, site.Id.ResourceGroupName ?? string.Empty, kind));
        }

        return [.. result.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    public async Task<AzureAppConfiguration> GetConfigurationAsync(
        AppServiceInfo appService, CancellationToken cancellationToken)
    {
        var site = CreateClient().GetWebSiteResource(new ResourceIdentifier(appService.ResourceId));

        var settings = await site.GetApplicationSettingsAsync(cancellationToken);
        var connectionStrings = await site.GetConnectionStringsAsync(cancellationToken);

        return new AzureAppConfiguration(
            new Dictionary<string, string>(settings.Value.Properties, StringComparer.OrdinalIgnoreCase),
            connectionStrings.Value.Properties
                .Where(p => p.Value?.Value is not null)
                .ToDictionary(p => p.Key, p => p.Value.Value, StringComparer.OrdinalIgnoreCase));
    }

    private ArmClient CreateClient() => new(auth.GetCredential());
}

