// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;

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

        public static async Task<HttpResponseMessage> WarmUp(HttpRequestMessage request, WebScriptHostManager scriptHostManager)
        {
            var queryParams = request.GetQueryParameterDictionary();
            string value = null;
            if (queryParams.TryGetValue("restart", out value) && string.Compare("1", value) == 0)
            {
                scriptHostManager.RestartHost();
                await scriptHostManager.DelayUntilHostReady();
            }

            await StandbyManager.WarmUp(scriptHostManager.Instance);

            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        public static bool IsWarmUpRequest(HttpRequestMessage request)
        {
            return ScriptSettingsManager.Instance.IsAzureEnvironment &&
                WebScriptHostManager.InStandbyMode &&
                request.IsAntaresInternalRequest() &&
                (request.MatchRoute($"api/{WarmUpFunctionName}") || request.MatchRoute($"api/{WarmUpAlternateRoute}"));
        }

        public static async Task WarmUp(ScriptHost host)
        {
            // exercise the Node pipeline
            await NodeFunctionInvoker.InitializeAsync();
        }

        public static void Initialize(ScriptHostConfiguration config)
        {
            CreateStandbyFunctions(config.RootScriptPath, config.TraceWriter);
        }

        private static void CreateStandbyFunctions(string scriptPath, TraceWriter traceWriter)
        {
            lock (_syncLock)
            {
                traceWriter.Info($"Creating StandbyMode placeholder function directory ({scriptPath})");

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

                traceWriter.Info($"StandbyMode placeholder function directory created");
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