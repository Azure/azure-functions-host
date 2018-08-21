// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    /// <summary>
    /// Contains methods related to standby mode (placeholder) app initialization.
    /// </summary>
    public static class StandbyManager
    {
        private const string WarmUpFunctionName = "WarmUp";
        private const string WarmUpAlternateRoute = "CSharpHttpWarmup";
        private static CancellationTokenSource _standbyCancellationTokenSource = new CancellationTokenSource();
        private static IChangeToken _standbyChangeToken = new CancellationChangeToken(_standbyCancellationTokenSource.Token);
        private static SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public static async Task<HttpResponseMessage> WarmUp(HttpRequest request, IScriptHostManager scriptHostManager)
        {
            if (request.Query.TryGetValue("restart", out StringValues value) && string.Compare("1", value) == 0)
            {
                await scriptHostManager.RestartHostAsync(CancellationToken.None);

                // This call is here for sanity, but we should be fully initialized.
                await scriptHostManager.DelayUntilHostReady();
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        public static bool IsWarmUpRequest(HttpRequest request, IScriptWebHostEnvironment webHostEnvironment)
        {
            return webHostEnvironment.InStandbyMode &&
                ((SystemEnvironment.Instance.IsAppServiceEnvironment() && request.IsAntaresInternalRequest()) || SystemEnvironment.Instance.IsLinuxContainerEnvironment()) &&
                (request.Path.StartsWithSegments(new PathString($"/api/{WarmUpFunctionName}")) ||
                request.Path.StartsWithSegments(new PathString($"/api/{WarmUpAlternateRoute}")));
        }

        public static IChangeToken GetChangeToken() => _standbyChangeToken;

        internal static void NotifyChange()
        {
            var tokenSource = Interlocked.Exchange(ref _standbyCancellationTokenSource, null);

            if (tokenSource != null &&
                !tokenSource.IsCancellationRequested)
            {
                var changeToken = Interlocked.Exchange(ref _standbyChangeToken, NullChangeToken.Singleton);

                tokenSource.Cancel();

                // Dispose of the token source so our change
                // token reflects that state
                tokenSource.Dispose();
            }
        }

        public static async Task InitializeAsync(ScriptApplicationHostOptions options, ILogger logger)
        {
            await CreateStandbyFunctionsAsync(options.ScriptPath, logger);
        }

        private static async Task CreateStandbyFunctionsAsync(string scriptPath, ILogger logger)
        {
            if (await _semaphore.WaitAsync(timeout: TimeSpan.FromSeconds(30)))
            {
                try
                {
                    logger.LogInformation($"Creating StandbyMode placeholder function directory ({scriptPath})");

                    await FileUtility.DeleteDirectoryAsync(scriptPath, true);
                    FileUtility.EnsureDirectoryExists(scriptPath);

                    string content = FileUtility.ReadResourceString($"{ScriptConstants.ResourcePath}.Functions.host.json");
                    File.WriteAllText(Path.Combine(scriptPath, "host.json"), content);

                    string functionPath = Path.Combine(scriptPath, WarmUpFunctionName);
                    Directory.CreateDirectory(functionPath);
                    content = FileUtility.ReadResourceString($"{ScriptConstants.ResourcePath}.Functions.{WarmUpFunctionName}.function.json");
                    File.WriteAllText(Path.Combine(functionPath, "function.json"), content);
                    content = FileUtility.ReadResourceString($"{ScriptConstants.ResourcePath}.Functions.{WarmUpFunctionName}.run.csx");
                    File.WriteAllText(Path.Combine(functionPath, "run.csx"), content);

                    logger.LogInformation($"StandbyMode placeholder function directory created");
                }
                finally
                {
                    _semaphore.Release();
                }
            }
        }
    }
}