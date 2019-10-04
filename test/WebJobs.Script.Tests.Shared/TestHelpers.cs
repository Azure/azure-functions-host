// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.OutOfProc.Http;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.WindowsAzure.Storage;
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

        public static async Task RunWithTimeoutAsync(Func<Task> action, TimeSpan timeout)
        {
            Task timeoutTask = Task.Delay(timeout);
            Task actionTask = action();
            Task completedTask = await Task.WhenAny(actionTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                throw new Exception($"Task did not complete within timeout interval {timeout}.");
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

        public static IList<WorkerConfig> GetTestWorkerConfigs()
        {
            var nodeWorkerDesc = GetTestWorkerDescription("node", ".js");
            var javaWorkerDesc = GetTestWorkerDescription("java", ".jar");

            return new List<WorkerConfig>()
            {
                new WorkerConfig() { Description = nodeWorkerDesc },
                new WorkerConfig() { Description = javaWorkerDesc },
            };
        }

        public static IList<WorkerConfig> GetTestWorkerConfigsNoLanguage()
        {
            var workerDesc = new RpcWorkerDescription();

            return new List<WorkerConfig>()
            {
                new WorkerConfig() { Description = workerDesc }
            };
        }

        public static string CreateOfflineFile()
        {
            // create a test offline file
            var offlineFilePath = Path.Combine(Path.GetTempPath(), ScriptConstants.AppOfflineFileName);
            string content = FileUtility.ReadResourceString($"{ScriptConstants.ResourcePath}.{ScriptConstants.AppOfflineFileName}", typeof(HttpException).Assembly);
            File.WriteAllText(offlineFilePath, content);
            return offlineFilePath;
        }

        public static void DeleteTestFile(string testFile)
        {
            if (File.Exists(testFile))
            {
                try
                {
                    File.Delete(testFile);
                }
                catch
                {
                    // best effort cleanup
                }
            }
        }

        public static RpcWorkerDescription GetTestWorkerDescription(string language, string extension)
        {
            return new RpcWorkerDescription()
            {
                Extensions = new List<string>()
                 {
                     { extension }
                 },
                Language = language
            };
        }

        public static IOptionsMonitor<T> CreateOptionsMonitor<T>() where T : class, new()
        {
            return CreateOptionsMonitor<T>(new T());
        }

        public static IOptionsMonitor<T> CreateOptionsMonitor<T>(T options) where T : class, new()
        {
            var factory = new TestOptionsFactory<T>(options);
            return new OptionsMonitor<T>(factory, Array.Empty<IOptionsChangeTokenSource<T>>(), factory);
        }

        public static async Task CreateContentZip(string contentRoot, string zipPath, params string[] copyDirs)
        {
            var contentTemp = Path.Combine(contentRoot, @"ZipContent");
            await FileUtility.DeleteDirectoryAsync(contentTemp, true);

            foreach (var sourceDir in copyDirs)
            {
                var directoryName = Path.GetFileName(sourceDir);
                var targetPath = Path.Combine(contentTemp, directoryName);
                FileUtility.EnsureDirectoryExists(targetPath);
                var sourcePath = Path.Combine(Directory.GetCurrentDirectory(), sourceDir);
                FileUtility.CopyDirectory(sourcePath, targetPath);
            }

            FileUtility.DeleteFileSafe(zipPath);
            ZipFile.CreateFromDirectory(contentTemp, zipPath);
        }

        public static async Task<Uri> CreateBlobSas(string connectionString, string filePath, string blobContainer, string blobName)
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);
            var blobClient = storageAccount.CreateCloudBlobClient();
            var container = blobClient.GetContainerReference(blobContainer);
            await container.CreateIfNotExistsAsync();
            var blob = container.GetBlockBlobReference(blobName);
            if (!string.IsNullOrEmpty(filePath))
            {
                await blob.UploadFromFileAsync(filePath);
            }
            var policy = new SharedAccessBlobPolicy
            {
                SharedAccessStartTime = DateTime.UtcNow,
                SharedAccessExpiryTime = DateTime.UtcNow.AddHours(1),
                Permissions = SharedAccessBlobPermissions.Read | SharedAccessBlobPermissions.List
            };
            var sas = blob.GetSharedAccessSignature(policy);
            var sasUri = new Uri(blob.Uri, sas);

            return sasUri;
        }

        public static async Task<Uri> CreateBlobContainerSas(string connectionString, string blobContainer)
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);
            var blobClient = storageAccount.CreateCloudBlobClient();
            var container = blobClient.GetContainerReference(blobContainer);
            await container.CreateIfNotExistsAsync();

            var policy = new SharedAccessBlobPolicy
            {
                SharedAccessStartTime = DateTime.UtcNow,
                SharedAccessExpiryTime = DateTime.UtcNow.AddHours(1),
                Permissions = SharedAccessBlobPermissions.Read | SharedAccessBlobPermissions.Write | SharedAccessBlobPermissions.List | SharedAccessBlobPermissions.Delete
            };
            var sas = container.GetSharedAccessSignature(policy);

            return new Uri(container.StorageUri.PrimaryUri, sas);
        }
    }
}
