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
            try
            {
                // The host uses version 5.5 of this assembly, but the deps.json says that the app
                // should use 5.6.
                Type t55 = Dependency55.TypeGetter.ReturnSecurityKeyType();
                Type t56 = Dependency56.TypeGetter.ReturnSecurityKeyType();

                if (!Equals(t55, t56) || !Equals(t55.Assembly, t56.Assembly))
                {
                    throw new InvalidOperationException($"{t55.FullName} does not equal {t56.FullName}.");
                }
            }
            catch (Exception ex)
            {
                return new ObjectResult(ex.ToString())
                {
                    StatusCode = 500
                };
            }

            return new OkResult();
        }
    }
}
