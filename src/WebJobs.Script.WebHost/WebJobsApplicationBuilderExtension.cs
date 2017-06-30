using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.DependencyInjection;

namespace WebJobs.Script.WebHost.Core
{
    public static class WebJobsApplicationBuilderExtension
    {
        public static IApplicationBuilder UseWebJobsScriptHost(this IApplicationBuilder builder, IApplicationLifetime applicationLifetime)
        {
            WebScriptHostManager hostManager = builder.ApplicationServices.GetService(typeof(WebScriptHostManager)) as WebScriptHostManager;

           


            

            return builder;
        }
    }
}
