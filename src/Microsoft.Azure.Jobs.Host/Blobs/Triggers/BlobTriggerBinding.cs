// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Blobs.Bindings;
using Microsoft.Azure.Jobs.Host.Blobs.Listeners;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Indexers;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs.Triggers
{
    internal class BlobTriggerBinding : ITriggerBinding<ICloudBlob>
    {
        private readonly string _parameterName;
        private readonly IArgumentBinding<ICloudBlob> _argumentBinding;
        private readonly CloudBlobClient _client;
        private readonly string _accountName;
        private readonly IBlobPathSource _path;
        private readonly IObjectToTypeConverter<ICloudBlob> _converter;

        public BlobTriggerBinding(string parameterName, IArgumentBinding<ICloudBlob> argumentBinding,
            CloudBlobClient client, IBlobPathSource path)
        {
            _parameterName = parameterName;
            _argumentBinding = argumentBinding;
            _client = client;
            _accountName = BlobClient.GetAccountName(client);
            _path = path;
            _converter = CreateConverter(client);
        }

        public IReadOnlyDictionary<string, Type> BindingDataContract
        {
            get { return _path.CreateBindingDataContract(); }
        }

        public string ContainerName
        {
            get { return _path.ContainerNamePattern; }
        }

        public string BlobName
        {
            get { return _path.BlobNamePattern; }
        }

        public string BlobPath
        {
            get { return _path.ToString(); }
        }

        private FileAccess Access
        {
            get
            {
                return typeof(ICloudBlob).IsAssignableFrom(_argumentBinding.ValueType)
                    ? FileAccess.ReadWrite : FileAccess.Read;
            }
        }

        private static IObjectToTypeConverter<ICloudBlob> CreateConverter(CloudBlobClient client)
        {
            return new CompositeObjectToTypeConverter<ICloudBlob>(
                new OutputConverter<ICloudBlob>(new IdentityConverter<ICloudBlob>()),
                new OutputConverter<string>(new StringToCloudBlobConverter(client)));
        }

        public ITriggerData Bind(ICloudBlob value, FunctionBindingContext context)
        {
            IValueProvider valueProvider = _argumentBinding.Bind(value, context);
            IReadOnlyDictionary<string, object> bindingData = CreateBindingData(value);

            return new TriggerData(valueProvider, bindingData);
        }

        public ITriggerData Bind(object value, FunctionBindingContext context)
        {
            ICloudBlob blob = null;

            if (!_converter.TryConvert(value, out blob))
            {
                throw new InvalidOperationException("Unable to convert trigger to ICloudBlob.");
            }

            return Bind(blob, context);
        }

        public IFunctionDefinition CreateFunctionDefinition(IReadOnlyDictionary<string, IBinding> nonTriggerBindings,
            FunctionDescriptor functionDescriptor, MethodInfo method)
        {
            ITriggeredFunctionBinding<ICloudBlob> functionBinding =
                new TriggeredFunctionBinding<ICloudBlob>(_parameterName, this, nonTriggerBindings);
            ITriggeredFunctionInstanceFactory<ICloudBlob> instanceFactory =
                new TriggeredFunctionInstanceFactory<ICloudBlob>(functionBinding, functionDescriptor, method);
            CloudBlobContainer container = _client.GetContainerReference(_path.ContainerNamePattern);
            IEnumerable<IBindableBlobPath> blobOutputs = GetBlobOutputs(nonTriggerBindings.Values);
            IListenerFactory listenerFactory = new BlobListenerFactory(container, _path, blobOutputs, instanceFactory);
            return new FunctionDefinition(instanceFactory, listenerFactory);
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return new BlobTriggerParameterDescriptor
            {
                Name = _parameterName,
                AccountName = _accountName,
                ContainerName = _path.ContainerNamePattern,
                BlobName = _path.BlobNamePattern,
                Access = Access
            };
        }

        private IReadOnlyDictionary<string, object> CreateBindingData(ICloudBlob value)
        {
            return _path.CreateBindingData(value.ToBlobPath());
        }

        private IEnumerable<IBindableBlobPath> GetBlobOutputs(IEnumerable<IBinding> nonTriggerBindingValues)
        {
            return nonTriggerBindingValues
                .OfType<BlobBinding>()
                .Where(b => b.Access == FileAccess.Write)
                .Select(b => b.Path);
        }
    }
}
