// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddWebJobsScriptHostAuthentication();
            services.AddWebJobsScriptHostAuthorization();
            services.AddWebJobsScriptHost(Configuration);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory)
        {
            if (Configuration.IsExternalWorkerEnabled())
            {
                var startupLogger = loggerFactory.CreateLogger(LogCategories.Startup);

                startupLogger.LogInformation(
                    "External worker mode enabled at startup. '{settingName}' is set, so the root host registered external worker services.",
                    EnvironmentSettingNames.FunctionsWorkerExternalEnabled);
            }

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseWebJobsScriptHost();
        }
    }
}
