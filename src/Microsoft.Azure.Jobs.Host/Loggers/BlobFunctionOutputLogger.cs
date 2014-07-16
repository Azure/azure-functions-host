// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Loggers
{
    internal class BlobFunctionOutputLogger : IFunctionOutputLogger
    {
        private readonly CloudBlobDirectory _outputLogDirectory;

        public BlobFunctionOutputLogger(CloudBlobClient client)
            : this(client.GetContainerReference(
                HostContainerNames.Hosts).GetDirectoryReference(HostDirectoryNames.OutputLogs))
        {
        }

        private BlobFunctionOutputLogger(CloudBlobDirectory outputLogDirectory)
        {
            _outputLogDirectory = outputLogDirectory;
        }

        public IFunctionOutputDefinition Create(IFunctionInstance instance)
        {
            _outputLogDirectory.Container.CreateIfNotExists();

            string namePrefix = instance.Id.ToString("N");

            LocalBlobDescriptor outputBlob = CreateDescriptor(_outputLogDirectory, namePrefix + ".txt");
            LocalBlobDescriptor parameterLogBlob = CreateDescriptor(_outputLogDirectory, namePrefix + ".params.txt");

            return new BlobFunctionOutputDefinition(_outputLogDirectory.ServiceClient, outputBlob, parameterLogBlob);
        }

        private static LocalBlobDescriptor CreateDescriptor(CloudBlobDirectory directory, string name)
        {
            CloudBlockBlob blob = directory.GetBlockBlobReference(name);

            return new LocalBlobDescriptor
            {
                ContainerName = blob.Container.Name,
                BlobName = blob.Name
            };
        }
    }
}
