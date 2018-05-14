#r "Newtonsoft.Json"
#r "../bin/Microsoft.Xrm.Sdk.dll"
#r "../bin/Microsoft.Azure.BlueRidge.Xrm.Extension.dll"

using Microsoft.Xrm.Sdk;
using Microsoft.Azure.BlueRidge.Xrm.Extension;

public class ProductInfo
{
    public string Category { get; set; }
    public int? Id { get; set; }
}

public static ProductInfo Run(ProductInfo info, string category, int? id, TraceWriter log)
{
    log.Info($"ProductInfo: Category={info.Category} Id={info.Id}");
    log.Info($"Parameters: category={category} id={id}");
    Entity entity = GetEntity(info) as Entity;
    IOrganizationService service = new MockOrganizationService();
    service.Create(entity);
    return info;
}

public static object GetEntity(ProductInfo info)
{
    Entity entity = new Entity();
    entity.Attributes["Category"] = info.Category;
    return entity;
}