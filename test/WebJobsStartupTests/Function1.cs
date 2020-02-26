using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Options;

namespace WebJobsStartupTests
{
    public class Function1
    {
        private readonly IMyService _myService;
        private readonly MyOptions _myOptions;
        private readonly IConfiguration _config;

        public Function1(IMyService myService, IOptions<MyOptions> myOptions, IConfiguration config)
        {
            _myService = myService;
            _myOptions = myOptions.Value;
            _config = config;
        }

        [FunctionName("Function1")]
        public IActionResult Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req)
        {
            if (!(_myService is MyService))
            {
                throw new InvalidOperationException();
            }

            if (_myOptions.MyKey != "MyValue" || _myOptions.MyOtherKey != "FromEnvironment")
            {
                throw new InvalidOperationException();
            }

            if (_config["SomeOtherKey"] != "SomeOtherValue")
            {
                throw new InvalidOperationException();
            }

            if (!ValidateConfig(_config))
            {
                throw new InvalidOperationException();
            }

            return new OkObjectResult("ok");
        }

        private static bool ValidateConfig(IConfiguration _config)
        {
            if (_config is ConfigurationRoot root)
            {
                if (root.Providers.Count() != 7)
                {
                    return false;
                }

                int i = 0;

                return
                    root.Providers.ElementAt(i++) is ChainedConfigurationProvider &&
                    root.Providers.ElementAt(i++) is MemoryConfigurationProvider &&
                    root.Providers.ElementAt(i++).GetType().Name.StartsWith("HostJsonFile") &&
                    root.Providers.ElementAt(i++) is JsonConfigurationProvider &&
                    root.Providers.ElementAt(i++) is EnvironmentVariablesConfigurationProvider &&
                    root.Providers.ElementAt(i++) is MemoryConfigurationProvider && // From Startup.cs
                    root.Providers.ElementAt(i++) is JsonConfigurationProvider; // From test settings; Always runs last in tests.
            }

            return false;
        }
    }
}