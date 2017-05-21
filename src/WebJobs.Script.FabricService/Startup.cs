using System.Web.Http;
using Owin;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.Config;
using Autofac;
using Microsoft.Azure.WebJobs.Script.WebHost.Controllers;
using Autofac.Integration.WebApi;

namespace WebJobs.Script.FabricService
{
    public static class Startup
    {
        // This code configures Web API. The Startup class is specified as a type
        // parameter in the WebApp.Start method.
        public static void ConfigureApp(IAppBuilder appBuilder)
        {
            // adapted from WebHost
            ScriptSettingsManager settingsManager = ScriptSettingsManager.Instance;
            WebHostSettings settings = WebHostSettings.CreateDefault(settingsManager);
            HttpConfiguration config = new HttpConfiguration();

            var builder = new ContainerBuilder();
            builder.RegisterApiControllers(typeof(FunctionsController).Assembly);
            AutofacBootstrap.Initialize(settingsManager, builder, settings);



            // Web API routes
            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute(
                name: "Home",
                routeTemplate: string.Empty,
                defaults: new { controller = "Home" });

            config.Routes.MapHttpRoute(
                name: "Functions",
                routeTemplate: "{*uri}",
                defaults: new { controller = "Functions" });

            appBuilder.UseWebApi(config);
        }
    }
}
