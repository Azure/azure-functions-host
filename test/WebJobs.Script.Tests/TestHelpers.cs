// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.WindowsAzure.Storage.Blob;

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

        public static async Task Await(Func<Task<bool>> condition, int timeout = 60 * 1000, int pollingInterval = 2 * 1000)
        {
            DateTime start = DateTime.Now;
            while (!await condition())
            {
                await Task.Delay(pollingInterval);

                if ((DateTime.Now - start).TotalMilliseconds > timeout)
                {
                    throw new ApplicationException("Condition not reached within timeout.");
                }
            }
        }

        public static string RemoveByteOrderMark(string s)
        {
            string byteOrderMark = Encoding.UTF8.GetString(Encoding.UTF8.GetPreamble());
            return s.Replace(byteOrderMark, string.Empty);
        }

        public static string RemoveByteOrderMarkAndWhitespace(string s)
        {
            string byteOrderMark = Encoding.UTF8.GetString(Encoding.UTF8.GetPreamble());
            return s.Trim().Replace(" ", string.Empty).Replace(byteOrderMark, string.Empty);
        }

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

        public static async Task<Collection<string>> GetFunctionLogsAsync(string functionName, bool throwOnNoLogs = true)
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

            return null;
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

        public static HttpServer CreateTestServer(string scriptRoot)
        {
            HttpConfiguration config = new HttpConfiguration();

            var hostSettings = new WebHostSettings
            {
                IsSelfHost = true,
                ScriptPath = scriptRoot,
                LogPath = Path.Combine(Path.GetTempPath(), @"Functions"),
                SecretsPath = Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\src\WebJobs.Script.WebHost\App_Data\Secrets")
            };
            WebApiConfig.Register(config, hostSettings);

            return new HttpServer(config);
        }
    }
}
