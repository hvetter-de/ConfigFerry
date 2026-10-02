using ConfigFerry.Core.Azure;
using ConfigFerry.Core.Models;

namespace ConfigFerry.Core.Tests;

[TestClass]
public class ArmMappingTests
{
    [TestMethod]
    [DataRow("functionapp", AppServiceKind.FunctionApp)]
    [DataRow("functionapp,linux", AppServiceKind.FunctionApp)]
    [DataRow("FunctionApp,Linux,container", AppServiceKind.FunctionApp)]
    [DataRow("functionapp,workflowapp", AppServiceKind.FunctionApp)]
    [DataRow("app", AppServiceKind.WebApp)]
    [DataRow("app,linux", AppServiceKind.WebApp)]
    [DataRow("api", AppServiceKind.WebApp)]
    [DataRow("", AppServiceKind.WebApp)]
    [DataRow(null, AppServiceKind.WebApp)]
    public void GetKind_RecognisesFunctionApps(string? kind, AppServiceKind expected) =>
        Assert.AreEqual(expected, ArmMapping.GetKind(kind));

    [TestMethod]
    public void ToTenant_WithoutId_ReturnsNull() =>
        Assert.IsNull(ArmMapping.ToTenant(null, "Contoso", "contoso.com"));

    [TestMethod]
    public void ToTenant_NameAndDomain_AreCombined()
    {
        var id = Guid.NewGuid();

        var tenant = ArmMapping.ToTenant(id, "Contoso", "contoso.onmicrosoft.com");

        Assert.AreEqual(new TenantInfo(id.ToString(), "Contoso (contoso.onmicrosoft.com)"), tenant);
    }

    [TestMethod]
    public void ToTenant_NameEqualToDomain_IsNotRepeated()
    {
        var id = Guid.NewGuid();

        Assert.AreEqual("contoso.com", ArmMapping.ToTenant(id, "contoso.com", "contoso.com")!.DisplayName);
    }

    [TestMethod]
    public void ToTenant_FallsBackToDomain_ThenId()
    {
        var id = Guid.NewGuid();

        Assert.AreEqual("contoso.com", ArmMapping.ToTenant(id, null, "contoso.com")!.DisplayName);
        Assert.AreEqual("contoso.com", ArmMapping.ToTenant(id, " ", "contoso.com")!.DisplayName);
        Assert.AreEqual(id.ToString(), ArmMapping.ToTenant(id, null, null)!.DisplayName);
    }

    [TestMethod]
    public void ToValueMap_DropsNullValues_AndIsCaseInsensitive()
    {
        var map = ArmMapping.ToValueMap(
        [
            KeyValuePair.Create("A", (string?)"1"),
            KeyValuePair.Create("B", (string?)null),
            KeyValuePair.Create("c", (string?)"3"),
        ]);

        Assert.AreEqual(2, map.Count);
        Assert.AreEqual("1", map["a"]);
        Assert.AreEqual("3", map["C"]);
        Assert.IsFalse(map.ContainsKey("B"));
    }
}
