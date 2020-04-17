using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Middleware;
using Microsoft.Azure.WebJobs.Script.Tests.Controllers;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Middleware.EasyAuth
{
    public class EasyAuthScenarioTestFixture : ControllerScenarioTestFixture
    {
        protected override void ConfigureWebHostBuilder(IWebHostBuilder webHostBuilder)
        {
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
            webHostBuilder.ConfigureAppConfiguration((hostContext, config) =>
            {
                config.AddEnvironmentVariables(envVars => new Dictionary<string, string>()
                {
                    { EnvironmentSettingNames.EasyAuthClientId, "23jekfs" },
                    { EnvironmentSettingNames.EasyAuthEnabled, "true" },
                    { EnvironmentSettingNames.ContainerName, "linuxconsumption" },
                    { EnvironmentSettingNames.EasyAuthSigningKey, "2892B532EB2C17AC3DD2009CBBF9C9CA7A3F9189FA4241789A4E26DE859077C0" },
                    { EnvironmentSettingNames.WebSiteAuthEncryptionKey, "723249EF012A5FCE5946F65FBE7D6CB209331612E651B638C2F46BF9DB39F530" }
                });
            });
            //    .ConfigureWebHostDefaults({
            //    webHostBuilder.UseStartup<Startup>();
            //});
        }
    }
}
