using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

public static async Task<IActionResult> Run(HttpRequest req)
{
    if (req.Query.TryGetValue("delay", out StringValues values))
    {
        int delay = int.Parse(values.ToString());
        await Task.Delay(delay);
    }

    return new OkObjectResult("Hello from Functions!");
} 