#r "Newtonsoft.Json"

using System;
using System.Net;
using System.Net.Http;
using Microsoft.Azure.WebJobs.Host;

public class ProductInfo
{
    public string Category { get; set; }
    public int Id { get; set; }
}

public static ProductInfo Run(ProductInfo info, string category, string id, TraceWriter log)
{
    log.Info($"category: {category} id: {id}");

    return info;
}