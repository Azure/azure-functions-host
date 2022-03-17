using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

// Note: Requires AzureWebJobsFeatureFlags=AllowSynchronousIO for env variables
public static async Task<IActionResult> Run(HttpRequest req)
{
    var html = new HtmlDocument();
    html.Load(req.Body);
    var root = html.DocumentNode;
    var nodes = root.Descendants();
    var totalNodes = nodes.Count();

    return (ActionResult)new OkObjectResult($"{totalNodes} parsed!");
}