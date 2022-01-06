#r "Newtonsoft.Json"

public class ProductInfo
{
    public string Category { get; set; }
    public int? Id { get; set; }
}

public static ProductInfo Run(ProductInfo info, string category, int? id, string extra, ILogger log)
{
    log.LogInformation($"ProductInfo: Category={info.Category} Id={info.Id}");
    log.LogInformation($"Parameters: category={category} id={id} extra={extra}");

    return info;
}