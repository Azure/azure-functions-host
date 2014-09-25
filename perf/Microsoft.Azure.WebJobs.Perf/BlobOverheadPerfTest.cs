// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.VisualStudio.Diagnostics.Measurement;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Perf
{
    public static partial class BlobOverheadPerfTest
    {
        private const string NamePrefix = "Overhead";

        private const string BlobLoggingOverheadMetric = NamePrefix + "-Logging";
        private const string BlobNoLoggingOverheadMetric = NamePrefix + "-NoLogging";

        private const string ContainerName = "blob-overhead-%rnd%";
        private const string TestBlobNameIn = NamePrefix + ".in";

        private const string TestWebJobsBlobNameOut = NamePrefix + ".webjobsSDK.out";
        private const string TestAzureBlobNameOut = NamePrefix + ".azureSDK.out";

        private static RandomNameResolver _nameResolver = new RandomNameResolver();

        private static string _connectionString;
        private static CloudStorageAccount _storageAccount;
        private static CloudBlobClient _blobClient;

        public static void Run(string connectionString, bool disableLogging)
        {
            _connectionString = connectionString;
            _storageAccount = CloudStorageAccount.Parse(connectionString);
            _blobClient = _storageAccount.CreateCloudBlobClient();

            Console.WriteLine("Creating the test blob...");
            CreateTestBlob();

            try
            {
                TimeSpan azureSDKTime = RunAzureSDKTest();
                TimeSpan webJobsSDKTime = RunWebJobsSDKTest(disableLogging);

                // Convert to ulong because the measurment block does not support other data type
                ulong perfRatio = (ulong)((webJobsSDKTime.TotalMilliseconds / azureSDKTime.TotalMilliseconds) * 100);

                Console.WriteLine("--- Results ---");
                Console.WriteLine("Azure SDK:   {0} ms: ", azureSDKTime.TotalMilliseconds);
                Console.WriteLine("WebJobs SDK: {0} ms: ", webJobsSDKTime.TotalMilliseconds);

                Console.WriteLine("Perf ratio (x100, long): {0}", perfRatio);

                MeasurementBlock.Mark(
                    perfRatio,
                    (disableLogging ? BlobNoLoggingOverheadMetric : BlobLoggingOverheadMetric) + ";Ratio;Percent");
            }
            finally
            {
                Cleanup();
            }
        }

        #region Azure SDK test

        private static TimeSpan RunAzureSDKTest()
        {
            Console.WriteLine("Running the Azure SDK test...");

            TimeBlock block = new TimeBlock();

            RunAzureSDKTestInternal();

            block.End();
            return block.ElapsedTime;
        }

        private static void RunAzureSDKTestInternal()
        {
            CloudBlobContainer container = _blobClient.GetContainerReference(_nameResolver.ResolveInString(ContainerName));
            CloudBlockBlob inBlob = container.GetBlockBlobReference(TestBlobNameIn);

            string blobContent = inBlob.DownloadText();

            CloudBlockBlob outBlob = container.GetBlockBlobReference(TestAzureBlobNameOut);
            outBlob.UploadText(blobContent);
        }

        #endregion

        #region WebJobs SDK test

        private static TimeSpan RunWebJobsSDKTest(bool disableLogging)
        {
            Console.WriteLine("Running the WebJobs SDK test...");

            TimeBlock block = new TimeBlock();

            RunWebJobsSDKTestInternal(disableLogging);

            block.End();
            return block.ElapsedTime;
        }

        private static void RunWebJobsSDKTestInternal(bool disableLogging)
        {
            JobHostConfiguration hostConfig = new JobHostConfiguration(_connectionString);
            hostConfig.NameResolver = _nameResolver;
            hostConfig.TypeLocator = new FakeTypeLocator(typeof(BlobOverheadPerfTest));

            if (disableLogging)
            {
                hostConfig.DashboardConnectionString = null;
            }

            JobHost host = new JobHost(hostConfig);
            host.Call(typeof(BlobOverheadPerfTest).GetMethod("BlobToBlob"));
        }

        [NoAutomaticTrigger]
        public static void BlobToBlob(
            [Blob(ContainerName + "/" + TestBlobNameIn)] string input,
            [Blob(ContainerName + "/" + TestWebJobsBlobNameOut)] out string output)
        {
            output = input;
        }

        #endregion

        private static void CreateTestBlob()
        {
            CloudBlobContainer container = _blobClient.GetContainerReference(_nameResolver.ResolveInString(ContainerName));
            container.CreateIfNotExists();

            CloudBlockBlob blob = container.GetBlockBlobReference(TestBlobNameIn);

            using (Stream ms = GenerateRandomText(sizeInMb: 1))
            {
                blob.UploadFromStream(ms);
            }
        }

        private static void Cleanup()
        {
            CloudBlobContainer container = _blobClient.GetContainerReference(_nameResolver.ResolveInString(ContainerName));
            container.DeleteIfExists();
        }

        private static Stream GenerateRandomText(long sizeInMb)
        {
            byte[] data = new byte[sizeInMb * 1024 * 1024];

            Random rnd = new Random();

            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (byte)rnd.Next('A', 'Z');
            }

            return new MemoryStream(data);
        }
    }
}
