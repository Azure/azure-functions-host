// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;

            if (ScriptSettingsManager.Instance.IsLinuxContainerEnvironment)
            {
                // Linux containers always start out in placeholder mode
                ScriptSettingsManager.Instance.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            }
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            services.AddWebJobsScriptHostAuthentication();
            services.AddWebJobsScriptHostAuthorization();

            return services.AddWebJobsScriptHost(Configuration);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IApplicationLifetime applicationLifetime, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            if (env.IsDevelopment())
            {
                loggerFactory.AddConsole(LogLevel.Trace, true);
                app.UseDeveloperExceptionPage();
            }

            app.UseWebJobsScriptHost(applicationLifetime);
        }
    }
}
