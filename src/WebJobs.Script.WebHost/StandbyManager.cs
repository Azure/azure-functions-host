// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Extensions;
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
        private static object _syncLock = new object();

        public static async Task<HttpResponseMessage> WarmUp(HttpRequest request, WebScriptHostManager scriptHostManager)
        {
            if (request.Query.TryGetValue("restart", out StringValues value) && string.Compare("1", value) == 0)
            {
                scriptHostManager.RestartHost();
                await scriptHostManager.DelayUntilHostReady();
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        public static bool IsWarmUpRequest(HttpRequest request)
        {
            return WebScriptHostManager.InStandbyMode &&
                ((ScriptSettingsManager.Instance.IsAppServiceEnvironment && request.IsAntaresInternalRequest()) || ScriptSettingsManager.Instance.IsLinuxContainerEnvironment) &&
                (request.Path.StartsWithSegments(new PathString($"/api/{WarmUpFunctionName}")) ||
                request.Path.StartsWithSegments(new PathString($"/api/{WarmUpAlternateRoute}")));
        }

        public static void Initialize(ScriptHostConfiguration config, ILogger logger)
        {
            CreateStandbyFunctions(config.RootScriptPath, logger);
        }

        private static void CreateStandbyFunctions(string scriptPath, ILogger logger)
        {
            lock (_syncLock)
            {
                logger.LogInformation($"Creating StandbyMode placeholder function directory ({scriptPath})");

                FileUtility.DeleteDirectoryAsync(scriptPath, true).GetAwaiter().GetResult();
                FileUtility.EnsureDirectoryExists(scriptPath);

                string content = ReadResourceString("Functions.host.json");
                File.WriteAllText(Path.Combine(scriptPath, "host.json"), content);

                string functionPath = Path.Combine(scriptPath, WarmUpFunctionName);
                Directory.CreateDirectory(functionPath);
                content = ReadResourceString($"Functions.{WarmUpFunctionName}.function.json");
                File.WriteAllText(Path.Combine(functionPath, "function.json"), content);
                content = ReadResourceString($"Functions.{WarmUpFunctionName}.run.csx");
                File.WriteAllText(Path.Combine(functionPath, "run.csx"), content);

                logger.LogInformation($"StandbyMode placeholder function directory created");
            }
        }

        private static string ReadResourceString(string fileName)
        {
            string resourcePath = string.Format("Microsoft.Azure.WebJobs.Script.WebHost.Resources.{0}", fileName);
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (StreamReader reader = new StreamReader(assembly.GetManifestResourceStream(resourcePath)))
            {
                return reader.ReadToEnd();
            }
        }
    }
}