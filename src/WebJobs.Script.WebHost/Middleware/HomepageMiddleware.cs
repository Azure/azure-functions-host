// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Features;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public class HomepageMiddleware
    {
        private readonly RequestDelegate _next;

        public HomepageMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public static bool IsHomepageDisabled
        {
            get
            {
                return string.Equals(ScriptSettingsManager.Instance.GetSetting(EnvironmentSettingNames.AzureWebJobsDisableHomepage),
                    bool.TrueString, StringComparison.OrdinalIgnoreCase);
            }
        }

        public static bool IsHomepageRequest(HttpContext context)
        {
            return context.Request.Path.Value == "/";
        }

        public async Task Invoke(HttpContext context)
        {
            await _next(context);

            var functionExecution = context.Features.Get<IFunctionExecutionFeature>();
            if (functionExecution == null && IsHomepageRequest(context))
            {
                IActionResult result = null;
                if (IsHomepageDisabled || context.Request.IsAppServiceInternalRequest())
                {
                    result = new NoContentResult();
                }
                else
                {
                    result = new ContentResult()
                    {
                        Content = GetHomepage(),
                        ContentType = "text/html",
                        StatusCode = 200
                    };
                }

                if (!context.Response.HasStarted)
                {
                    var actionContext = new ActionContext
                    {
                        HttpContext = context
                    };

                    await result.ExecuteResultAsync(actionContext);
                }
            }
        }

        private string GetHomepage()
        {
            var assembly = typeof(HomepageMiddleware).Assembly;
            using (Stream resource = assembly.GetManifestResourceStream(assembly.GetName().Name + ".Home.html"))
            using (var reader = new StreamReader(resource))
            {
                return reader.ReadToEnd();
            }
        }
    }
}
