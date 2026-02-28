// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Diagnostics.JitTrace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public class HostWarmupMiddleware
    {
        private readonly IWebHostWorkerManager _workerManager;
        private readonly IOptions<FunctionsHostingConfigOptions> _hostingConfigOptions;
        private readonly IHttpClientFactory _httpClientFactory;
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
            IWebHostWorkerManager workerManager,
            IOptions<FunctionsHostingConfigOptions> hostingConfigOptions,
            IHttpClientFactory httpClientFactory = null)
        {
            _next = next;
            _webHostEnvironment = webHostEnvironment;
            _environment = environment;
            _hostManager = hostManager;
            _logger = logger;
            _assemblyLocalPath = Path.GetDirectoryName(new Uri(typeof(HostWarmupMiddleware).Assembly.Location).LocalPath);
            _workerManager = workerManager ?? throw new ArgumentNullException(nameof(workerManager));
            _hostingConfigOptions = hostingConfigOptions;
            _httpClientFactory = httpClientFactory;
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
                await PreJitPrepareAsync(WarmUpConstants.JitTraceFileName, WarmUpConstants.PreJitTraceUrlSettingName);
                if (_environment.IsAnyLinuxConsumption())
                {
                    await PreJitPrepareAsync(WarmUpConstants.LinuxJitTraceFileName, WarmUpConstants.PreJitLinuxTraceUrlSettingName);
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
            await _workerManager.WorkerWarmupAsync();
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

        internal async Task PreJitPrepareAsync(string jitTraceFileName, string urlSettingName)
        {
            StreamReader remoteStream = await TryDownloadJitTraceAsync(jitTraceFileName, urlSettingName);

            if (remoteStream is not null)
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    JitTraceRuntime.Prepare(remoteStream, out int successfulPrepares, out int failedPrepares);
                    sw.Stop();
                    _logger.LogInformation(new EventId(100, "PreJit"),
                        "PreJIT (remote) Successful prepares: {successfulPrepares}, Failed prepares: {failedPrepares} FileName = {jitTraceFileName}, Duration = {elapsedMs}ms",
                        successfulPrepares, failedPrepares, jitTraceFileName, sw.ElapsedMilliseconds);

                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to apply remote jittrace for '{jitTraceFileName}'. Falling back to local file.", jitTraceFileName);
                }
                finally
                {
                    remoteStream.Dispose();
                }
            }

            PreJitPrepare(jitTraceFileName);
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
                var sw = Stopwatch.StartNew();
                JitTraceRuntime.Prepare(file, out int successfulPrepares, out int failedPrepares);
                sw.Stop();

                // We will need to monitor failed vs success prepares and if the failures increase, it means code paths have diverged or there have been updates on dotnet core side.
                // When this happens, we will need to regenerate the coldstart.jittrace file.
                _logger.LogInformation(new EventId(100, "PreJit"),
                    "PreJIT (local fallback) Successful prepares: {successfulPrepares}, Failed prepares: {failedPrepares} FileName = {jitTraceFileName}, Duration = {elapsedMs}ms",
                    successfulPrepares, failedPrepares, jitTraceFileName, sw.ElapsedMilliseconds);
            }
        }

        /// <summary>
        /// Attempts to download a .jittrace file from a URL configured via FunctionsHostingConfig or environment variable.
        /// Downloads the file to a well-known local path under the PreJIT folder. If the file has already been
        /// downloaded from a previous run, the local copy is reused without re-downloading.
        /// Returns a <see cref="StreamReader"/> if a remote file is available, or <c>null</c> if no URL is configured or the download fails.
        /// The caller is responsible for disposing the returned <see cref="StreamReader"/>.
        /// </summary>
        internal async Task<StreamReader> TryDownloadJitTraceAsync(string jitTraceFileName, string urlSettingName)
        {
            string url = _hostingConfigOptions?.Value?.GetFeature(urlSettingName)
                         ?? _environment.GetEnvironmentVariable(urlSettingName);

            if (string.IsNullOrEmpty(url))
            {
                return null;
            }

            var localPath = Path.Combine(
                _assemblyLocalPath,
                WarmUpConstants.PreJitFolderName, $"remote.{jitTraceFileName}");

            // If we already downloaded this file previously, reuse it.
            if (File.Exists(localPath))
            {
                _logger.LogInformation("Using cached remote jittrace file at '{localPath}' (source: cached).", localPath);

                return new StreamReader(new FileStream(localPath, FileMode.Open, FileAccess.Read));
            }

            if (_httpClientFactory is null)
            {
                _logger.LogWarning("IHttpClientFactory is not available. Cannot download remote jittrace file for '{jitTraceFileName}'.", jitTraceFileName);

                return null;
            }

            try
            {
                _logger.LogInformation("Downloading remote jittrace file for '{jitTraceFileName}' from configured URL (source: remote).", jitTraceFileName);

                var sw = Stopwatch.StartNew();
                var httpClient = _httpClientFactory.CreateClient();
                using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                using (var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write))
                {
                    await response.Content.CopyToAsync(fileStream);
                }

                sw.Stop();
                _logger.LogInformation("Successfully downloaded remote jittrace file for '{jitTraceFileName}' to '{localPath}' (source: remote, downloadMs: {elapsedMs}).",
                    jitTraceFileName, localPath, sw.ElapsedMilliseconds);

                return new StreamReader(new FileStream(localPath, FileMode.Open, FileAccess.Read));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download remote jittrace file for '{jitTraceFileName}'. Falling back to local file.", jitTraceFileName);

                return null;
            }
        }

        public async Task HostWarmupAsync(HttpRequest request)
        {
            if (request.Query.TryGetValue("restart", out StringValues value) && string.Compare("1", value) == 0)
            {
                await _hostManager.RestartHostAsync("Host warmup call requested a restart.", CancellationToken.None);

                // This call is here for sanity, but we should be fully initialized.
                await _hostManager.DelayUntilHostReadyAsync();
            }
        }

        public static bool IsWarmUpRequest(HttpRequest request, bool inStandbyMode, IEnvironment environment)
        {
            // Check if the request is a warmup request in placeholder simulation mode
            if (Utility.IsInPlaceholderSimulationMode && inStandbyMode &&
                (request.Path.StartsWithSegments(_warmupRoutePath, StringComparison.OrdinalIgnoreCase) || request.Path.StartsWithSegments(_warmupRouteAlternatePath, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return inStandbyMode
                && ((environment.IsAppService() && request.IsAppServiceInternalRequest(environment)) || environment.IsAnyLinuxConsumption())
                && (request.Path.StartsWithSegments(_warmupRoutePath) || request.Path.StartsWithSegments(_warmupRouteAlternatePath));
        }
    }
}
