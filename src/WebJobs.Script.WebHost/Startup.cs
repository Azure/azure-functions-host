// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Script.Composition;
using Microsoft.Azure.WebJobs.Script.WebHost.Composition;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class Startup
    {
        private readonly IWorkerComposition _composition;

        public Startup(IConfiguration configuration)
            : this(configuration, ServerWorkerComposition.Instance)
        {
        }

        internal Startup(IConfiguration configuration, IWorkerComposition composition)
        {
            Configuration = configuration;
            _composition = composition ?? throw new ArgumentNullException(nameof(composition));
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddWebJobsScriptHostAuthentication();
            services.AddWebJobsScriptHostAuthorization();
            services.AddWebJobsScriptHost(Configuration, _composition);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseWebJobsScriptHost();
        }
    }
}
