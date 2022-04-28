// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Diagnostics.JitTrace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public class HostWarmupMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IScriptWebHostEnvironment _webHostEnvironment;
        private readonly IEnvironment _environment;
        private readonly IScriptHostManager _hostManager;
        private readonly ILogger _logger;
        private string _assemblyLocalPath;

        public HostWarmupMiddleware(RequestDelegate next, IScriptWebHostEnvironment webHostEnvironment, IEnvironment environment, IScriptHostManager hostManager, ILogger<HostWarmupMiddleware> logger)
        {
            _next = next;
            _webHostEnvironment = webHostEnvironment;
            _environment = environment;
            _hostManager = hostManager;
            _logger = logger;
            _assemblyLocalPath = Path.GetDirectoryName(new Uri(typeof(HostWarmupMiddleware).Assembly.CodeBase).LocalPath);
        }

        public async Task Invoke(HttpContext httpContext)
        {
            if (IsWarmUpRequest(httpContext.Request, _webHostEnvironment.InStandbyMode, _environment))
            {
                PreJitPrepare(WarmUpConstants.JitTraceFileName);
                if (_environment.IsLinuxConsumption())
                {
                    PreJitPrepare(WarmUpConstants.LinuxJitTraceFileName);
                }

                ReadRuntimeAssemblyFiles();

                await WarmUp(httpContext.Request);
            }

            await _next.Invoke(httpContext);
        }

        internal void ReadRuntimeAssemblyFiles()
        {
            try
            {
                string[] allFiles = Directory.GetFiles(_assemblyLocalPath, "*.dll", SearchOption.TopDirectoryOnly);
                foreach (string file in allFiles)
                {
                    // Read file content to avoid disk reads during specialization. This is only to page-in bytes.
                    ReadFileInChunks(file);
                }
                _logger.LogDebug(new EventId(100, nameof(ReadRuntimeAssemblyFiles)), $"Number of files read: {allFiles.Count()}.");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(new EventId(100, nameof(ReadRuntimeAssemblyFiles)), ex, "Reading ReadRuntimeAssemblyFiles failed");
            }
        }

        private void ReadFileInChunks(string file)
        {
            try
            {
                // Read File content in 4K chunks
                const int MAX_BUFFER = 4 * 1024;
                byte[] chunk = new byte[MAX_BUFFER];
                int bytesRead;
                using (FileStream fileStream = new FileStream(file, FileMode.Open, FileAccess.Read))
                {
                    while ((bytesRead = fileStream.Read(chunk, 0, MAX_BUFFER)) != 0)
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(new EventId(100, nameof(ReadFileInChunks)), ex, $"Reading file '{file}' failed");
            }
        }

        private void PreJitPrepare(string jitTraceFileName)
        {
            // This is to PreJIT all methods captured in coldstart.jittrace file to improve cold start time
            var path = Path.Combine(
                _assemblyLocalPath,
                WarmUpConstants.PreJitFolderName, jitTraceFileName);

            var file = new FileInfo(path);

            if (file.Exists)
            {
                JitTraceRuntime.Prepare(file, out int successfulPrepares, out int failedPrepares);

                // We will need to monitor failed vs success prepares and if the failures increase, it means code paths have diverged or there have been updates on dotnet core side.
                // When this happens, we will need to regenerate the coldstart.jittrace file.
                _logger.LogInformation(new EventId(100, "PreJit"),
                    $"PreJIT Successful prepares: {successfulPrepares}, Failed prepares: {failedPrepares} FileName = {jitTraceFileName}");
            }
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

        public static bool IsWarmUpRequest(HttpRequest request, bool inStandbyMode, IEnvironment environment)
        {
            return inStandbyMode &&
                ((environment.IsAppService() && request.IsAppServiceInternalRequest(environment)) || environment.IsLinuxConsumption()) &&
                (request.Path.StartsWithSegments(new PathString($"/api/{WarmUpConstants.FunctionName}")) ||
                request.Path.StartsWithSegments(new PathString($"/api/{WarmUpConstants.AlternateRoute}")));
        }
    }
}
