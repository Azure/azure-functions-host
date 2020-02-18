// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Abstractions.Description;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    // These helpers are split out so it's easier to share the TestHelpers.cs file with EndToEnd tests that do
    // not have a direct dependency on anything WebJobs related.
    public static partial class TestHelpers
    {
        public static IHost GetDefaultHost(Action<ScriptApplicationHostOptions> configure = null)
        {
            if (configure == null)
            {
                configure = o =>
                {
                    o.ScriptPath = TestHelpers.FunctionsTestDirectory;
                    o.LogPath = TestHelpers.GetHostLogFileDirectory().FullName;
                };
            }

            return new HostBuilder()
                .ConfigureDefaultTestWebScriptHost(configure)
                .Build();
        }

        public static ScriptHost GetDefaultScriptHost(Action<ScriptApplicationHostOptions> configure = null)
        {
            return GetDefaultHost(configure)
                .GetScriptHost();
        }

        public static FunctionBinding CreateBindingFromHost(IHost host, JObject json)
        {
            var bindingProviders = host.Services.GetServices<IScriptBindingProvider>();
            var context = new ScriptBindingContext(json);

            ScriptBinding scriptBinding = null;
            bindingProviders.FirstOrDefault(p => p.TryCreate(context, out scriptBinding));

            if (scriptBinding != null)
            {
                BindingMetadata bindingMetadata = BindingMetadata.Create(json);
                var config = new ScriptJobHostOptions();
                return new ExtensionBinding(config, scriptBinding, bindingMetadata);
            }

            return null;
        }

        public static FunctionBinding CreateTestBinding(JObject json)
        {
            ScriptBindingContext context = new ScriptBindingContext(json);
            WebJobsCoreScriptBindingProvider provider = new WebJobsCoreScriptBindingProvider(NullLogger<WebJobsCoreScriptBindingProvider>.Instance);
            ScriptBinding scriptBinding = null;
            provider.TryCreate(context, out scriptBinding);
            BindingMetadata bindingMetadata = BindingMetadata.Create(json);
            var config = new ScriptJobHostOptions();
            return new ExtensionBinding(config, scriptBinding, bindingMetadata);
        }

        public static string RemoveByteOrderMarkAndWhitespace(string s) => Utility.RemoveUtf8ByteOrderMark(s).Trim().Replace(" ", string.Empty);

        public static async Task<IList<string>> GetFunctionLogsAsync(string functionName, bool throwOnNoLogs = true, bool waitForFlush = true)
        {
            if (waitForFlush)
            {
                await Task.Delay(FileWriter.LogFlushIntervalMs);
            }

            DirectoryInfo directory = GetFunctionLogFileDirectory(functionName);
            FileInfo lastLogFile = null;

            if (directory.Exists)
            {
                lastLogFile = directory.GetFiles("*.log").OrderByDescending(p => p.LastWriteTime).FirstOrDefault();
            }

            if (lastLogFile != null)
            {
                string[] logs = await ReadAllLinesSafeAsync(lastLogFile.FullName);
                return new Collection<string>(logs.ToList());
            }
            else if (throwOnNoLogs)
            {
                throw new InvalidOperationException("No logs written!");
            }

            return new Collection<string>();
        }

        public static async Task<IList<string>> GetHostLogsAsync(bool throwOnNoLogs = true)
        {
            await Task.Delay(FileWriter.LogFlushIntervalMs);

            DirectoryInfo directory = GetHostLogFileDirectory();
            FileInfo lastLogFile = directory.GetFiles("*.log").OrderByDescending(p => p.LastWriteTime).FirstOrDefault();

            if (lastLogFile != null)
            {
                string[] logs = File.ReadAllLines(lastLogFile.FullName);
                return new Collection<string>(logs.ToList());
            }
            else if (throwOnNoLogs)
            {
                throw new InvalidOperationException("No logs written!");
            }

            return new Collection<string>();
        }
    }
}
