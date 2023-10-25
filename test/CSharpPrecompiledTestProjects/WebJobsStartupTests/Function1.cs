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
            try
            {
                if (!(_myService is MyService))
                {
                    return new ObjectResult("_myService is not of type MyService")
                    {
                        StatusCode = 500
                    };
                }

                if (_myOptions.MyKey != "MyValue")
                {
                    return new ObjectResult($"_myOptions.MyKey is {_myOptions.MyKey}")
                    {
                        StatusCode = 500
                    };
                }

                if (_myOptions.MyOtherKey != "FromEnvironment")
                {
                    return new ObjectResult($"_myOptions.MyOtherKey is {_myOptions.MyOtherKey}")
                    {
                        StatusCode = 500
                    };

                }

                if (_config["SomeOtherKey"] != "SomeOtherValue")
                {
                    return new ObjectResult($"SomeOtherKey is {_config["SomeOtherKey"]}")
                    {
                        StatusCode = 500
                    };
                }

                if (!ValidateConfig(_config))
                {
                    return new ObjectResult("Configuration validation failed.")
                    {
                        StatusCode = 500
                    };
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

        // Use this to test overwriting a trigger parameter.
        [FunctionName("Timer")]
        public void TimerRun([TimerTrigger("%Cron%", RunOnStartup = false)] TimerInfo timerInfo)
        {
        }

        [FunctionName("Echo")]
        public IActionResult Echo([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req)
        {
            if (req.Query.TryGetValue("echo", out var value))
            {
                return new OkObjectResult(value.Single());
            }

            return new BadRequestResult();
        }

        private static bool ValidateConfig(IConfiguration _config)
        {
            if (_config is ConfigurationRoot root)
            {
                if (root.Providers.Count() != 8)
                {
                    return false;
                }

                int i = 0;

                return
                    root.Providers.ElementAt(i++) is ChainedConfigurationProvider &&
                    root.Providers.ElementAt(i++) is MemoryConfigurationProvider &&
                    root.Providers.ElementAt(i++).GetType().Name.StartsWith("HostJsonFile") &&
                    root.Providers.ElementAt(i++) is ChainedConfigurationProvider &&
                    root.Providers.ElementAt(i++) is JsonConfigurationProvider &&
                    root.Providers.ElementAt(i++) is EnvironmentVariablesConfigurationProvider &&
                    root.Providers.ElementAt(i++) is MemoryConfigurationProvider && // From Startup.cs
                    root.Providers.ElementAt(i++) is JsonConfigurationProvider; // From test settings; Always runs last in tests.
            }

            return false;
        }
    }
}