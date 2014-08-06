// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Blobs;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Indexers;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.IntegrationTests
{
    // Support local execution. This does not have a trigger service, but still maintains all of the logging and causality.
    // Exposes some of the logging objects so that callers can monitor what happened. 
    internal class LocalExecutionContext
    {
        private readonly Type _type;
        private readonly IFunctionIndexLookup _index;
        private readonly CloudBlobClient _blobClient;
        private readonly HostBindingContext _context;

        // Expose to allow callers to hook in new binders. 
        public LocalExecutionContext(Type type, CloudStorageAccount account, Type[] cloudBlobStreamBinderTypes)
        {
            _type = type;
            FunctionIndex index = FunctionIndex.CreateAsync(
                new FunctionIndexContext(null, null, account, null, CancellationToken.None),
                new Type[] { type }, cloudBlobStreamBinderTypes).Result;
            _index = index;

            _blobClient = account.CreateCloudBlobClient();
            _context = new HostBindingContext(
                bindingProvider: index.BindingProvider,
                nameResolver: null,
                storageAccount: account,
                serviceBusConnectionString: null);
        }

        public void Call(string functionName, object arguments = null)
        {
            IFunctionDefinition function = Lookup(functionName);
            var parametersDictionary = ObjectDictionaryConverter.AsDictionary(arguments);
            IFunctionInstance instance = function.InstanceFactory.Create(Guid.NewGuid(), null, ExecutionReason.HostCall,
                parametersDictionary);
            Execute(instance);
        }

        public void CallOnBlob(string functionName, string blobPath)
        {
            IFunctionDefinition function = Lookup(functionName);

            BlobPath parsed = BlobPath.Parse(blobPath);
            CloudBlobContainer container = _blobClient.GetContainerReference(parsed.ContainerName);
            CloudBlockBlob blobInput = container.GetBlockBlobReference(parsed.BlobName);

            ITriggeredFunctionInstanceFactory<ICloudBlob> instanceFactory = (ITriggeredFunctionInstanceFactory<ICloudBlob>)function.InstanceFactory;
            IFunctionInstance instance = instanceFactory.Create(blobInput, null);
            Execute(instance);
        }

        private IFunctionDefinition Lookup(string functionName)
        {
            return _index.Lookup(_type.FullName + "." + functionName);
        }

        private void Execute(IFunctionInstance instance)
        {
            ValueBindingContext context = new ValueBindingContext(new FunctionBindingContext(_context, instance.Id,
                CancellationToken.None, TextWriter.Null), CancellationToken.None);
            FunctionExecutor.ExecuteWithWatchersAsync(instance.Method, instance.Method.GetParameters(),
                instance.BindingSource.BindAsync(context).Result, TextWriter.Null,
                CancellationToken.None).GetAwaiter().GetResult();
        }
    }
}
