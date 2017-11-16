﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
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
        /// Gets the common root directory that functions tests create temporary directories under.
        /// This enables us to clean up test files by deleting this single directory.
        /// </summary>
        public static string FunctionsTestDirectory
        {
            get
            {
                return Path.Combine(Path.GetTempPath(), "FunctionsTest");
            }
        }

        public static Task Await(Func<bool> condition, int timeout = 60 * 1000, int pollingInterval = 2 * 1000, bool throwWhenDebugging = false)
        {
            return Await(() => Task.FromResult(condition()), timeout, pollingInterval, throwWhenDebugging);
        }

        public static async Task Await(Func<Task<bool>> condition, int timeout = 60 * 1000, int pollingInterval = 2 * 1000, bool throwWhenDebugging = false)
        {
            DateTime start = DateTime.Now;
            while (!await condition())
            {
                await Task.Delay(pollingInterval);

                bool shouldThrow = !Debugger.IsAttached || (Debugger.IsAttached && throwWhenDebugging);
                if (shouldThrow && (DateTime.Now - start).TotalMilliseconds > timeout)
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
            await TestHelpers.Await(async () =>
            {
                return await blob.ExistsAsync();
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

        /// <summary>
        /// Waits until a request sent via the specified HttpClient returns OK or NoContent, indicating
        /// that the host is ready to invoke functions.
        /// </summary>
        /// <param name="client">The HttpClient.</param>
        public static void WaitForWebHost(HttpClient client)
        {
            TestHelpers.Await(() =>
            {
                return IsHostRunning(client);
            }).Wait();
        }

        private static bool IsHostRunning(HttpClient client)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, string.Empty))
            {
                using (HttpResponseMessage response = client.SendAsync(request).Result)
                {
                    return response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.OK;
                }
            }
        }

        public static void ClearHostLogs()
        {
            DirectoryInfo directory = GetHostLogFileDirectory();
            if (directory.Exists)
            {
                foreach (var file in directory.GetFiles())
                {
                    try
                    {
                        file.Delete();
                    }
                    catch
                    {
                        // best effort
                    }
                }
            }
        }

        public static async Task<IList<string>> GetFunctionLogsAsync(string functionName, bool throwOnNoLogs = true, bool waitForFlush = true)
        {
            if (waitForFlush)
            {
                await Task.Delay(FileTraceWriter.LogFlushIntervalMs);
            }

            DirectoryInfo directory = GetFunctionLogFileDirectory(functionName);
            FileInfo lastLogFile = directory.GetFiles("*.log").OrderByDescending(p => p.LastWriteTime).FirstOrDefault();

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
            await Task.Delay(FileTraceWriter.LogFlushIntervalMs);

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

        // Deleting and recreating a container can result in a 409 as the container name is not
        // immediately available. Instead, use this helper to clear a container.
        public static async Task ClearContainer(CloudBlobContainer container)
        {
            BlobResultSegment blobs = await container.ListBlobsSegmentedAsync(null);

            foreach (IListBlobItem blobItem in blobs.Results)
            {
                CloudBlockBlob blockBlob = blobItem as CloudBlockBlob;
                if (blockBlob != null)
                {
                    await container.GetBlobReference(blockBlob.Name).DeleteIfExistsAsync();
                }
            }
        }

        public static DirectoryInfo GetFunctionLogFileDirectory(string functionName)
        {
            string path = Path.Combine(Path.GetTempPath(), "Functions", "Function", functionName);
            return new DirectoryInfo(path);
        }

        public static DirectoryInfo GetHostLogFileDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "Functions", "Host");
            return new DirectoryInfo(path);
        }

        public static FunctionBinding CreateTestBinding(JObject json)
        {
            ScriptBindingContext context = new ScriptBindingContext(json);
            WebJobsCoreScriptBindingProvider provider = new WebJobsCoreScriptBindingProvider(new JobHostConfiguration(), new JObject(), new TestTraceWriter(TraceLevel.Verbose));
            ScriptBinding scriptBinding = null;
            provider.TryCreate(context, out scriptBinding);
            BindingMetadata bindingMetadata = BindingMetadata.Create(json);
            ScriptHostConfiguration config = new ScriptHostConfiguration();
            return new ExtensionBinding(config, scriptBinding, bindingMetadata);
        }

        private static async Task<string[]> ReadAllLinesSafeAsync(string logFile)
        {
            // ReadAllLines won't work if the file is being written to.
            // So try a few more times.

            int count = 0;
            bool success = false;
            string[] logs = null;

            while (!success && count++ < 3)
            {
                try
                {
                    logs = File.ReadAllLines(logFile);
                    success = true;
                }
                catch (IOException)
                {
                    await Task.Delay(500);
                }
            }

            return logs;
        }


        public static async Task<string> ReadStreamToEnd(Stream stream)
        {
            stream.Position = 0;
            using (var sr = new StreamReader(stream))
            {
                return await sr.ReadToEndAsync();
            }
        }
    }
}
