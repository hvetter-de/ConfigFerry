using ConfigFerry.Core.Abstractions;
using ConfigFerry.Core.Models;
using global::Azure.Core;
using global::Azure.ResourceManager.AppService;
using global::Azure.ResourceManager.Resources;

namespace ConfigFerry.Core.Azure;

public sealed class AzureResourceService(IArmClientFactory armClients) : IAzureResourceService
{
    public async Task<IReadOnlyList<TenantInfo>> GetTenantsAsync(CancellationToken cancellationToken)
    {
        var result = new List<TenantInfo>();
        await foreach (var tenant in armClients.Create().GetTenants().GetAllAsync(cancellationToken))
        {
            var info = ArmMapping.ToTenant(tenant.Data.TenantId, tenant.Data.DisplayName, tenant.Data.DefaultDomain);
            if (info is not null)
            {
                result.Add(info);
            }
        }

        return [.. result.OrderBy(t => t.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
    }

    public async Task<IReadOnlyList<SubscriptionInfo>> GetSubscriptionsAsync(CancellationToken cancellationToken)
    {
        var result = new List<SubscriptionInfo>();
        await foreach (var subscription in armClients.Create().GetSubscriptions().GetAllAsync(cancellationToken))
        {
            result.Add(new SubscriptionInfo(subscription.Data.SubscriptionId, subscription.Data.DisplayName));
        }

        return [.. result.OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
    }

    public async Task<IReadOnlyList<AppServiceInfo>> GetAppServicesAsync(
        string subscriptionId, CancellationToken cancellationToken)
    {
        var client = armClients.Create();
        var subscription = client.GetSubscriptionResource(SubscriptionResource.CreateResourceIdentifier(subscriptionId));

        var result = new List<AppServiceInfo>();
        await foreach (var site in subscription.GetWebSitesAsync(cancellationToken))
        {
            result.Add(new AppServiceInfo(
                site.Id.ToString(), site.Data.Name, site.Id.ResourceGroupName ?? string.Empty, ArmMapping.GetKind(site.Data.Kind)));
        }

        return [.. result.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    public async Task<AzureAppConfiguration> GetConfigurationAsync(
        AppServiceInfo appService, CancellationToken cancellationToken)
    {
        var site = armClients.Create().GetWebSiteResource(new ResourceIdentifier(appService.ResourceId));

        var settings = await site.GetApplicationSettingsAsync(cancellationToken);
        var connectionStrings = await site.GetConnectionStringsAsync(cancellationToken);

        return new AzureAppConfiguration(
            ArmMapping.ToValueMap(settings.Value.Properties.Select(p => KeyValuePair.Create(p.Key, (string?)p.Value))),
            ArmMapping.ToValueMap(connectionStrings.Value.Properties.Select(p => KeyValuePair.Create(p.Key, p.Value?.Value))));
    }
}
