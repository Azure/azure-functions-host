// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
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
                    throw new ApplicationException(string.Format("Condition not reached within timeout:{0}.", timeout));
                }
            }
        }

        public static string RemoveByteOrderMarkAndWhitespace(string s)
        {
            string byteOrderMark = Encoding.UTF8.GetString(Encoding.UTF8.GetPreamble());
            return s.Trim().Replace(" ", string.Empty).Replace(byteOrderMark, string.Empty);
        }

        public static async Task<string> WaitForBlobAsync(CloudBlockBlob blob, int timeout = 60 * 1000)
        {
            await TestHelpers.Await(() =>
            {
                return blob.Exists();
            }, timeout);

            string result = await blob.DownloadTextAsync(Encoding.UTF8,
                null, new BlobRequestOptions(), new Microsoft.WindowsAzure.Storage.OperationContext());

            return result;
        }
    }
}
