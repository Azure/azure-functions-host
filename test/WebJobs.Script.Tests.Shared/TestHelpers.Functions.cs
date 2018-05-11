// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    // These helpers are split out so it's easier to share the TestHelpers.cs file with EndToEnd tests that do
    // not have a direct dependency on anything WebJobs related.
    public static partial class TestHelpers
    {
        public static FunctionBinding CreateTestBinding(JObject json)
        {
            ScriptBindingContext context = new ScriptBindingContext(json);
            WebJobsCoreScriptBindingProvider provider = new WebJobsCoreScriptBindingProvider(new JobHostConfiguration(), new JObject(), null);
            ScriptBinding scriptBinding = null;
            provider.TryCreate(context, out scriptBinding);
            BindingMetadata bindingMetadata = BindingMetadata.Create(json);
            ScriptHostConfiguration config = new ScriptHostConfiguration();
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
