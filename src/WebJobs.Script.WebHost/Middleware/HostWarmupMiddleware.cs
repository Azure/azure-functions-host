// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Diagnostics.JitTrace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public class HostWarmupMiddleware
    {
        private readonly IWebHostRpcWorkerChannelManager _webHostRpcWorkerChannelManager;
        private readonly IOptions<FunctionsHostingConfigOptions> _hostingConfigOptions;
        private readonly RequestDelegate _next;
        private readonly IScriptWebHostEnvironment _webHostEnvironment;
        private readonly IEnvironment _environment;
        private readonly IScriptHostManager _hostManager;
        private readonly ILogger _logger;
        private string _assemblyLocalPath;
        private volatile bool _jitTraceHasRun;

        private static readonly PathString _warmupRoutePath = new PathString($"/api/{WarmUpConstants.FunctionName}");
        private static readonly PathString _warmupRouteAlternatePath = new PathString($"/api/{WarmUpConstants.AlternateRoute}");

        public HostWarmupMiddleware(
            RequestDelegate next,
            IScriptWebHostEnvironment webHostEnvironment,
            IEnvironment environment,
            IScriptHostManager hostManager,
            ILogger<HostWarmupMiddleware> logger,
            IWebHostRpcWorkerChannelManager rpcWorkerChannelManager,
            IOptions<FunctionsHostingConfigOptions> hostingConfigOptions)
        {
            _next = next;
            _webHostEnvironment = webHostEnvironment;
            _environment = environment;
            _hostManager = hostManager;
            _logger = logger;
            _assemblyLocalPath = Path.GetDirectoryName(new Uri(typeof(HostWarmupMiddleware).Assembly.Location).LocalPath);
            _webHostRpcWorkerChannelManager = rpcWorkerChannelManager ?? throw new ArgumentNullException(nameof(rpcWorkerChannelManager));
            _hostingConfigOptions = hostingConfigOptions;
        }

        public Task Invoke(HttpContext httpContext)
        {
            if (_webHostEnvironment.InStandbyMode)
            {
                return WarmupInvoke(httpContext);
            }

            return _next.Invoke(httpContext);
        }

        /// <summary>
        /// This is so we only pay the async overhead while in the warmup path, but not for primary runtime.
        /// </summary>
        public async Task WarmupInvoke(HttpContext httpContext)
        {
            // We only want to run our JIT traces on the first warmup call.
            if (!_jitTraceHasRun)
            {
                PreJitPrepare(WarmUpConstants.JitTraceFileName);
                if (_environment.IsAnyLinuxConsumption())
                {
                    PreJitPrepare(WarmUpConstants.LinuxJitTraceFileName);
                }
                _jitTraceHasRun = true;
            }

            ReadRuntimeAssemblyFiles();

            await HostWarmupAsync(httpContext.Request);

            await WorkerWarmupAsync();

            await _next.Invoke(httpContext);
        }

        private async Task WorkerWarmupAsync()
        {
            await _webHostRpcWorkerChannelManager.WorkerWarmupAsync();
        }

        internal void ReadRuntimeAssemblyFiles()
        {
            try
            {
                string[] allFiles = Directory.GetFiles(_assemblyLocalPath, "*.dll", SearchOption.TopDirectoryOnly);
                // Read File content in 4K chunks
                int maxBuffer = 4 * 1024;
                byte[] chunk = new byte[maxBuffer];
                Random random = new Random();
                foreach (string file in allFiles)
                {
                    // Read file content to avoid disk reads during specialization. This is only to page-in bytes.
                    ReadFileInChunks(file, chunk, maxBuffer, random);
                }
                _logger.LogDebug(new EventId(100, nameof(ReadRuntimeAssemblyFiles)), "Number of files read: '{allFilesCount}'. AssemblyLocalPath: '{assemblyLocalPath}' ", allFiles.Count(), _assemblyLocalPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(new EventId(100, nameof(ReadRuntimeAssemblyFiles)), ex, "Reading ReadRuntimeAssemblyFiles failed. AssemblyLocalPath: '{assemblyLocalPath}'", _assemblyLocalPath);
            }
        }

        private void ReadFileInChunks(string file, byte[] chunk, int maxBuffer, Random random)
        {
            try
            {
                using (FileStream fileStream = new FileStream(file, FileMode.Open, FileAccess.Read))
                {
                    int bytesRead;
                    while ((bytesRead = fileStream.Read(chunk, 0, maxBuffer)) != 0)
                    {
                        // Read one random byte for every 4K bytes - 4K is default OS page size. This will help avoid disk read during specialization
                        // see for details on OS page buffering in Windows - https://docs.microsoft.com/en-us/windows/win32/fileio/file-buffering
                        var randomByte = Convert.ToInt32(chunk[random.Next(0, bytesRead - 1)]);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(new EventId(100, nameof(ReadFileInChunks)), ex, "Reading file '{file}' failed. AssemblyLocalPath: '{assemblyLocalPath}'", file, _assemblyLocalPath);
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

        public async Task HostWarmupAsync(HttpRequest request)
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
            // In placeholder simulation mode, we want the homepage request to also trigger warmup code.
            var isWarmupViaHomePageRequest = Utility.IsInPlaceholderSimulationMode && inStandbyMode && request.Path.Value == "/";
            if (isWarmupViaHomePageRequest)
            {
                return true;
            }

            return inStandbyMode
                && ((environment.IsAppService() && request.IsAppServiceInternalRequest(environment)) || environment.IsAnyLinuxConsumption())
                && (request.Path.StartsWithSegments(_warmupRoutePath) || request.Path.StartsWithSegments(_warmupRouteAlternatePath));
        }
    }
}
