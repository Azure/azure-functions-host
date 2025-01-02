using Microsoft.AspNetCore.Mvc;

namespace AspNetBenchmark.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class HelloController : ControllerBase
    {
        [HttpGet]
        public IActionResult Hello()
        {
            return new OkObjectResult("Hello from ASP.NET!");
        }
    }
}