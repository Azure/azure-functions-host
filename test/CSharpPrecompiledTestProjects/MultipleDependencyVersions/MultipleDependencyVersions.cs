using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;

namespace MultipleDependencyVersions
{
    public static class MultipleDependencyVersions
    {
        [FunctionName("MultipleDependencyVersions")]
        public static IActionResult Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req)
        {
            Type t56 = Dependency56.TypeGetter.ReturnSecurityKeyType();
            Type t55 = Dependency55.TypeGetter.ReturnSecurityKeyType();

            if (!Equals(t55, t56) || !Equals(t55.Assembly, t56.Assembly))
            {
                throw new InvalidOperationException();
            }

            return new OkResult();
        }
    }
}
