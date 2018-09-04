// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public class HostWarmupMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IScriptWebHostEnvironment _webHostEnvironment;
        private readonly IEnvironment _environment;
        private readonly IScriptHostManager _hostManager;

        public HostWarmupMiddleware(RequestDelegate next, IScriptWebHostEnvironment webHostEnvironment, IEnvironment environment, IScriptHostManager hostManager)
        {
            _next = next;
            _webHostEnvironment = webHostEnvironment;
            _environment = environment;
            _hostManager = hostManager;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            if (IsWarmUpRequest(httpContext.Request, _webHostEnvironment, _environment))
            {
                await WarmUp(httpContext.Request);
            }

            await _next.Invoke(httpContext);
        }

        public async Task WarmUp(HttpRequest request)
        {
            if (request.Query.TryGetValue("restart", out StringValues value) && string.Compare("1", value) == 0)
            {
                await _hostManager.RestartHostAsync(CancellationToken.None);

                // This call is here for sanity, but we should be fully initialized.
                await _hostManager.DelayUntilHostReady();
            }
        }

        public static bool IsWarmUpRequest(HttpRequest request, IScriptWebHostEnvironment webHostEnvironment, IEnvironment environment)
        {
            return webHostEnvironment.InStandbyMode &&
                ((environment.IsAppServiceEnvironment() && request.IsAntaresInternalRequest(environment)) || environment.IsLinuxContainerEnvironment()) &&
                (request.Path.StartsWithSegments(new PathString($"/api/{WarmUpConstants.FunctionName}")) ||
                request.Path.StartsWithSegments(new PathString($"/api/{WarmUpConstants.AlternateRoute}")));
        }
    }
}
