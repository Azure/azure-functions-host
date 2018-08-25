// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public static partial class TestHelpers
    {
        private const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        private static readonly Random Random = new Random();

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

        public static byte[] GenerateKeyBytes()
        {
            using (var aes = new AesManaged())
            {
                aes.GenerateKey();
                return aes.Key;
            }
        }

        public static string GenerateKeyHexString(byte[] key = null)
        {
            return BitConverter.ToString(key ?? GenerateKeyBytes()).Replace("-", string.Empty);
        }

        public static string NewRandomString(int length = 10)
        {
            return new string(
                Enumerable.Repeat('x', length)
                    .Select(c => Chars[Random.Next(Chars.Length)])
                    .ToArray());
        }

        public static Task Await(Func<bool> condition, int timeout = 30 * 1000, int pollingInterval = 50, bool throwWhenDebugging = false, Func<string> userMessageCallback = null)
        {
            return Await(() => Task.FromResult(condition()), timeout, pollingInterval, throwWhenDebugging, userMessageCallback);
        }

        public static async Task Await(Func<Task<bool>> condition, int timeout = 60 * 1000, int pollingInterval = 2 * 1000, bool throwWhenDebugging = false, Func<string> userMessageCallback = null)
        {
            DateTime start = DateTime.Now;
            while (!await condition())
            {
                await Task.Delay(pollingInterval);

                bool shouldThrow = !Debugger.IsAttached || (Debugger.IsAttached && throwWhenDebugging);
                if (shouldThrow && (DateTime.Now - start).TotalMilliseconds > timeout)
                {
                    string error = "Condition not reached within timeout.";
                    if (userMessageCallback != null)
                    {
                        error += " " + userMessageCallback();
                    }
                    throw new ApplicationException(error);
                }
            }
        }

        public static async Task<string> WaitForBlobAndGetStringAsync(CloudBlockBlob blob, Func<string> userMessageCallback = null)
        {
            await WaitForBlobAsync(blob, userMessageCallback: userMessageCallback);

            string result = await blob.DownloadTextAsync(Encoding.UTF8,
                null, new BlobRequestOptions(), new Microsoft.WindowsAzure.Storage.OperationContext());

            return result;
        }

        public static async Task WaitForBlobAsync(CloudBlockBlob blob, Func<string> userMessageCallback = null)
        {
            await TestHelpers.Await(async () =>
            {
                return await blob.ExistsAsync();
            }, userMessageCallback: userMessageCallback);
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

        public static IConfiguration GetTestConfiguration()
        {
            return new ConfigurationBuilder()
                    .AddEnvironmentVariables()
                    .AddTestSettings()
                    .Build();
        }

        // Deleting and recreating a container can result in a 409 as the container name is not
        // immediately available. Instead, use this helper to clear a container.
        public static async Task ClearContainerAsync(CloudBlobContainer container)
        {
            foreach (var blob in await ListBlobsAsync(container))
            {
                await blob.DeleteIfExistsAsync();
            }
        }

        public static async Task<IEnumerable<CloudBlockBlob>> ListBlobsAsync(CloudBlobContainer container)
        {
            List<CloudBlockBlob> blobs = new List<CloudBlockBlob>();
            BlobContinuationToken token = null;

            do
            {
                BlobResultSegment blobSegment = await container.ListBlobsSegmentedAsync(token);
                token = blobSegment.ContinuationToken;
                blobs.AddRange(blobSegment.Results.Cast<CloudBlockBlob>());
            }
            while (token != null);

            return blobs;
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

        public static IEnumerable<WorkerConfig> GetTestWorkerConfigs()
        {
            var nodeWorkerDesc = GetTestWorkerDescription("node", ".js");
            var javaWorkerDesc = GetTestWorkerDescription("java", ".jar");

            return new List<WorkerConfig>()
            {
                new WorkerConfig() { Description = nodeWorkerDesc },
                new WorkerConfig() { Description = javaWorkerDesc },
            };
        }

        public static WorkerDescription GetTestWorkerDescription(string language, string extension)
        {
            return new WorkerDescription()
            {
                Extensions = new List<string>()
                 {
                     { extension }
                 },
                Language = language
            };
        }
    }
}
