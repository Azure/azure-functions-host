#r "Newtonsoft.Json"

public class ProductInfo
{
    public string Category { get; set; }
    public int? Id { get; set; }
}

public static ProductInfo Run(ProductInfo info, string category, int? id, string extra, TraceWriter log)
{
    log.Info($"ProductInfo: Category={info.Category} Id={info.Id}");
    log.Info($"Parameters: category={category} id={id} extra={extra}");

    return info;
}