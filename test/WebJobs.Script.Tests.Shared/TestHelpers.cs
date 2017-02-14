// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public static class TestHelpers
    {
        /// <summary>
        /// Common root directory that functions tests create temporary directories under.
        /// This enables us to clean up test files by deleting this single directory.
        /// </summary>
        public static string FunctionsTestDirectory
        {
            get
            {
                return Path.Combine(Path.GetTempPath(), "FunctionsTest");
            }
        }

        public static async Task Await(Func<bool> condition, int timeout = 60 * 1000, int pollingInterval = 2 * 1000)
        {
            DateTime start = DateTime.Now;
            while (!condition())
            {
                await Task.Delay(pollingInterval);

                if ((DateTime.Now - start).TotalMilliseconds > timeout)
                {
                    throw new ApplicationException("Condition not reached within timeout.");
                }
            }
        }

        public static string RemoveByteOrderMarkAndWhitespace(string s) => Utility.RemoveUtf8ByteOrderMark(s).Trim().Replace(" ", string.Empty);

        public static async Task<string> WaitForBlobAndGetStringAsync(CloudBlockBlob blob)
        {
            await WaitForBlobAsync(blob);

            string result = await blob.DownloadTextAsync(Encoding.UTF8,
                null, new BlobRequestOptions(), new Microsoft.WindowsAzure.Storage.OperationContext());

            return result;
        }

        public static async Task WaitForBlobAsync(CloudBlockBlob blob)
        {
            await TestHelpers.Await(() =>
            {
                return blob.Exists();
            });
        }

        public static void ClearFunctionLogs(string functionName)
        {
            DirectoryInfo directory = GetFunctionLogFileDirectory(functionName);
            if (directory.Exists)
            {
                foreach (var file in directory.GetFiles())
                {
                    file.Delete();
                }
            }
        }

        public static async Task<IList<string>> GetFunctionLogsAsync(string functionName, bool throwOnNoLogs = true)
        {
            await Task.Delay(FileTraceWriter.LogFlushIntervalMs);

            DirectoryInfo directory = GetFunctionLogFileDirectory(functionName);
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

        // Deleting and recreating a container can result in a 409 as the container name is not
        // immediately available. Instead, use this helper to clear a container.
        public static void ClearContainer(CloudBlobContainer container)
        {
            foreach (IListBlobItem blobItem in container.ListBlobs())
            {
                CloudBlockBlob blockBlob = blobItem as CloudBlockBlob;
                if (blockBlob != null)
                {
                    container.GetBlobReference(blockBlob.Name).DeleteIfExists();
                }
            }
        }

        public static DirectoryInfo GetFunctionLogFileDirectory(string functionName)
        {
            string functionLogsPath = Path.Combine(Path.GetTempPath(), "Functions", "Function", functionName);
            return new DirectoryInfo(functionLogsPath);
        }

        public static FunctionBinding CreateTestBinding(JObject json)
        {
            ScriptBindingContext context = new ScriptBindingContext(json);
            WebJobsCoreScriptBindingProvider provider = new WebJobsCoreScriptBindingProvider(new JobHostConfiguration(), new JObject(), new TestTraceWriter(TraceLevel.Verbose));
            ScriptBinding scriptBinding = null;
            provider.TryCreate(context, out scriptBinding);
            BindingMetadata bindingMetadata = BindingMetadata.Create<BindingMetadata>(json);
            ScriptHostConfiguration config = new ScriptHostConfiguration();
            return new ExtensionBinding(config, scriptBinding, bindingMetadata);
        }
    }
}
