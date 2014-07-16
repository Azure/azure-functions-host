// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Configuration;
using System.IO;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Jobs;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;

namespace BlobOperations
{
    class Person
    {
        public string Name { get; set; }

        public int Age { get; set; }
    }

    class Program
    {
        /// <summary>
        /// Reads a blob from the container named "input" and writes it to the container named "output". The blob name ("name") is preserved
        /// </summary>
        public static void BlobToBlob([BlobInput("input/{name}")] TextReader input, [BlobOutput("output/{name}")] out string output)
        {
            output = input.ReadToEnd();
        }

        /// <summary>
        /// This function is triggered when a new blob is created by "BlobToBlob"
        /// </summary>
        public static void BlobTrigger([BlobInput("output/{name}")] Stream input)
        {
            using (StreamReader reader = new StreamReader(input))
            {
                Console.WriteLine("Blob content: {0}", reader.ReadToEnd());
            }
        }

        /// <summary>
        /// Reads a "Person" object from the "persons" queue
        /// The parameter "Name" will have the same value as the property "Name" of the person object
        /// The output blob will have the name of the "Name" property of the person object
        /// </summary>
        public static void BlobNameFromQueueMessage([QueueInput] Person persons, string Name, [BlobOutput("persons/{Name}BlobNameFromQueueMessage")] out string output)
        {
            output = "Hello " + Name;
        }

        /// <summary>
        /// Same as "BlobNameFromQueueMessage" but using IBinder 
        /// </summary>
        public static void BlobIBinder([QueueInput] Person persons, IBinder binder)
        {
            TextWriter writer = binder.Bind<TextWriter>(new BlobOutputAttribute("persons/" + persons.Name + "BlobIBinder"));
            writer.Write("Hello " + persons.Name);
        }

        /// <summary>
        /// Not writing anything into the output stream will not lead to blob creation
        /// </summary>
        public static void BlobCancelWrite([QueueInput] Person persons, [BlobOutput("output/ShouldNotBeCreated.txt")] TextWriter output)
        {
            // Do not write anything to "output" and the blob will not be created
        }

        static void Main()
        {
            CreateDemoData();

            JobHost host = new JobHost();
            host.RunAndBlock();
        }

        private static void CreateDemoData()
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(ConfigurationManager.ConnectionStrings["AzureJobsData"].ConnectionString);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference("input");
            container.CreateIfNotExist();

            CloudBlob blob = container.GetBlobReference("BlobOperations");
            blob.UploadText("Hello world!");

            CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();
            CloudQueue queue = queueClient.GetQueueReference("persons");
            queue.CreateIfNotExist();

            Person person = new Person()
            {
                Name = "John",
                Age = 42
            };

            queue.AddMessage(new CloudQueueMessage(JsonConvert.SerializeObject(person)));
        }
    }
}
