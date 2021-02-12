using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Hosting;

namespace ReferenceOlderRuntimeAssembly
{
    public class ReferenceOlderRuntimeAssembly
    {
        private readonly IHostingEnvironment _env;

        public ReferenceOlderRuntimeAssembly(IHostingEnvironment env)
        {
            _env = env;
        }

        [FunctionName("ReferenceOlderRuntimeAssembly")]
        public IActionResult Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req)
        {
            if (_env == null)
            {
                return new ObjectResult("IHostingEnvironment was not injected into the function class.")
                {
                    StatusCode = 500
                };
            }

            return new OkResult();
        }
    }
}
