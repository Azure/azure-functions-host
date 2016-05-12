// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public static class TestHelpers
    {
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
            foreach (var file in directory.GetFiles())
            {
                file.Delete();
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

        public static DirectoryInfo GetFunctionLogFileDirectory(string functionName)
        {
            string functionLogsPath = Path.Combine(Path.GetTempPath(), "Functions", "Function", functionName);
            return new DirectoryInfo(functionLogsPath);
        }
    }
}
