using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

public static async Task<IActionResult> Run(HttpRequest req, ILogger log)
{
    string content = await new StreamReader(req.Body).ReadToEndAsync();
    var html = new HtmlDocument();
    html.LoadHtml(content);
    var root = html.DocumentNode;
    var nodes = root.Descendants();
    var totalNodes = nodes.Count();

    return (ActionResult)new OkObjectResult($"{totalNodes} parsed!");
}