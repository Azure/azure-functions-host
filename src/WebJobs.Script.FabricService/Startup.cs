using Autofac;
using Autofac.Integration.WebApi;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.FabricHost;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Controllers;
using Microsoft.Azure.WebJobs.Script.WebHost.Handlers;
using Microsoft.Diagnostics.EventFlow;
using Microsoft.Diagnostics.EventFlow.Inputs;
using Microsoft.Extensions.Logging;
using Owin;
using System.Fabric;
using System.IO;
using System.Web.Http;

namespace WebJobs.Script.FabricService
{
    public static class Startup
    {
        public static ServiceContext ServiceContext { get; set; }

        public static void ConfigureApp(IAppBuilder appBuilder)
        {
            ScriptSettingsManager settingsManager = ScriptSettingsManager.Instance;
            WebHostSettings settings = FabricServiceSettings.CreateDefault(settingsManager,ServiceContext);
            HttpConfiguration config = new HttpConfiguration();

            var builder = new ContainerBuilder();
            builder.RegisterApiControllers(typeof(FunctionsController).Assembly);
            AutofacBootstrap.Initialize(settingsManager, builder, settings);

            var container = builder.Build();
            config.DependencyResolver = new AutofacWebApiDependencyResolver(container);
            config.Formatters.Add(new PlaintextMediaTypeFormatter());
            config.Services.Replace(typeof(System.Web.Http.ExceptionHandling.IExceptionHandler), new ExceptionProcessingHandler(config));
            AddMessageHandlers(config);

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

            // TODO: add Initialize WebHook Receivers

            var scriptHostManager = config.DependencyResolver.GetService<WebScriptHostManager>();
            if (scriptHostManager != null && !scriptHostManager.Initialized)
            {
                scriptHostManager.Initialize();
            }

            // TODO: add EventFlow pipeline to Logger
            var configPackage = ServiceContext.CodePackageActivationContext.GetConfigurationPackageObject("Config");
            var pipeline = DiagnosticPipelineFactory.CreatePipeline(Path.Combine(configPackage.Path, "eventFlowConfig.json"));
            //ILoggerFactory loggerFactory = config.DependencyResolver.GetService<ILoggerFactory>();
            //loggerFactory.AddEventFlow(pipeline);


        }

        private static void AddMessageHandlers(HttpConfiguration config)
        {
            config.MessageHandlers.Add(new WebScriptHostHandler(config));
            config.MessageHandlers.Add(new SystemTraceHandler(config));
        }
    }
}
