using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Middleware;
using Microsoft.Azure.WebJobs.Script.Tests.Controllers;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Middleware.EasyAuth
{
    public class EasyAuthScenarioTestFixture : ControllerScenarioTestFixture
    {
        protected override void ConfigureWebHostBuilder(IWebHostBuilder webHostBuilder)
        {
            // TODO - as iterate, figure out what need to mock
            base.ConfigureWebHostBuilder(webHostBuilder);

            webHostBuilder.Configure(app =>
            {
                app.UseMiddleware<JobHostPipelineMiddleware>();
                app.Run(async context =>
                {
                    await context.Response.WriteAsync("test easy auth");
                });
            }).ConfigureServices(services =>
            {
                services.ConfigureOptions<HostEasyAuthOptionsSetup>();
                services.Configure<HostEasyAuthOptions>(options =>
                {
                    options.SiteAuthEnabled = true;
                    options.SiteAuthClientId = "23jekfs";
                });
                services.TryAddSingleton<IJobHostMiddlewarePipeline, DefaultMiddlewarePipeline>();
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHostHttpMiddleware, JobHostEasyAuthMiddleware>());
            }).UseStartup<Startup>();
           // webHostBuilder.ConfigureAppConfiguration();
        }
    }
}
