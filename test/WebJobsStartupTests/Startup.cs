using System.Collections.Generic;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebJobsStartupTests;

[assembly: WebJobsStartup(typeof(Startup))]

namespace WebJobsStartupTests
{
    public class Startup : IWebJobsStartup, IWebJobsConfigurationStartup
    {
        public void Configure(IWebJobsBuilder builder)
        {
            builder.Services.AddSingleton<IMyService, MyService>();
            builder.Services.AddOptions<MyOptions>()
                .Configure<IConfiguration>((options, config) =>
                {
                    config.GetSection("MyOptions").Bind(options);
                });
        }

        public void Configure(IWebJobsConfigurationBuilder builder)
        {
            builder.ConfigurationBuilder.AddInMemoryCollection(new Dictionary<string, string>
            {
                { "MyOptions:MyKey", "MyValue" },
                { "SomeOtherKey", "SomeOtherValue" }
            });
        }
    }

    public interface IMyService { }

    public class MyService : IMyService { }

    public class MyOptions
    {
        public string MyKey { get; set; }

        public string MyOtherKey { get; set; }
    }
}
