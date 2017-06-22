using System.Net;
using Microsoft.AspNetCore.Mvc;

public static IActionResult Run(string req,TraceWriter log)
{
    return new OkObjectResult(new Test { Name = "blah" });
}

public class Test
{
    public string Name { get; set; }
}