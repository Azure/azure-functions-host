// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Bindings
{
    internal class BlobContainerBinding : IBinding
    {
        private readonly string _parameterName;
        private readonly IArgumentBinding<IStorageBlobContainer> _argumentBinding;
        private readonly IStorageBlobClient _client;
        private readonly string _accountName;
        private readonly IBindableBlobPath _path;

        public BlobContainerBinding(string parameterName, IArgumentBinding<IStorageBlobContainer> argumentBinding, IStorageBlobClient client,
            IBindableBlobPath path)
        {
            _parameterName = parameterName;
            _argumentBinding = argumentBinding;
            _client = client;
            _accountName = BlobClient.GetAccountName(client);
            _path = path;
        }

        public bool FromAttribute
        {
            get { return true; }
        }

        public string ContainerName
        {
            get { return _path.ContainerNamePattern; }
        }

        public string BlobName
        {
            get { return _path.BlobNamePattern; }
        }

        public IBindableBlobPath Path
        {
            get { return _path; }
        }

        public FileAccess Access
        {
            get { return FileAccess.Read; }
        }

        public async Task<IValueProvider> BindAsync(BindingContext context)
        {
            BlobPath boundPath = _path.Bind(context.BindingData);
            IStorageBlobContainer container = _client.GetContainerReference(boundPath.ContainerName);
            ValueBindingContext containerContext = new BlobContainerValueBindingContext(boundPath, context.ValueContext);

            return await BindBlobContainerAsync(container, containerContext);
        }

        public async Task<IValueProvider> BindAsync(object value, ValueBindingContext context)
        {
            BlobPath path = null;
            IStorageBlobContainer container = null;

            if (TryConvert(value, _client, out container, out path))
            {
                return await BindBlobContainerAsync(container, new BlobContainerValueBindingContext(path, context));
            }
            
            throw new InvalidOperationException("Unable to convert value to CloudBlobContainer.");
        }

        internal static bool TryConvert(object value, IStorageBlobClient client, out IStorageBlobContainer container, out BlobPath path)
        {
            container = null;
            path = null;

            string fullPath = value as string;
            if (fullPath != null)
            {
                path = BlobPath.ParseAndValidate(fullPath, isContainerBinding: true);
                container = client.GetContainerReference(path.ContainerName);
                return true;
            }

            return false;
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return new BlobParameterDescriptor
            {
                Name = _parameterName,
                AccountName = _accountName,
                ContainerName = _path.ContainerNamePattern,
                BlobName = _path.BlobNamePattern,
                Access = Access
            };
        }

        internal static void ValidateContainerBinding(BlobAttribute attribute, Type parameterType, IBindableBlobPath path)
        {
            if (attribute.Access != null && attribute.Access != FileAccess.Read)
            {
                throw new InvalidOperationException("Only the 'Read' FileAccess mode is supported for blob container bindings.");
            }

            if (parameterType == typeof(CloudBlobContainer) && !string.IsNullOrEmpty(path.BlobNamePattern))
            {
                throw new InvalidOperationException("Only a container name can be specified when binding to CloudBlobContainer.");
            }
        }

        private Task<IValueProvider> BindBlobContainerAsync(IStorageBlobContainer value, ValueBindingContext context)
        {
            return _argumentBinding.BindAsync(value, context);
        }

        internal class BlobContainerValueBindingContext : ValueBindingContext
        {
            public BlobContainerValueBindingContext(BlobPath path, ValueBindingContext context)
                : base(context.FunctionContext, context.CancellationToken)
            {
                Path = path;
            }

            public BlobPath Path { get; private set; }
        }
    }
}
