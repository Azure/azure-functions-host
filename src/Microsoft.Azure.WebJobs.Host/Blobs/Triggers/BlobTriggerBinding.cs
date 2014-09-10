// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Blobs.Listeners;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Triggers
{
    internal class BlobTriggerBinding : ITriggerBinding<ICloudBlob>
    {
        private readonly string _parameterName;
        private readonly IArgumentBinding<ICloudBlob> _argumentBinding;
        private readonly CloudBlobClient _client;
        private readonly string _accountName;
        private readonly IBlobPathSource _path;
        private readonly IAsyncObjectToTypeConverter<ICloudBlob> _converter;
        private readonly IReadOnlyDictionary<string, Type> _bindingDataContract;

        public BlobTriggerBinding(string parameterName, IArgumentBinding<ICloudBlob> argumentBinding,
            CloudBlobClient client, IBlobPathSource path)
        {
            _parameterName = parameterName;
            _argumentBinding = argumentBinding;
            _client = client;
            _accountName = BlobClient.GetAccountName(client);
            _path = path;
            _converter = CreateConverter(client);
            _bindingDataContract = CreateBindingDataContract(path);
        }

        public IReadOnlyDictionary<string, Type> BindingDataContract
        {
            get { return _bindingDataContract; }
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

        private static IReadOnlyDictionary<string, Type> CreateBindingDataContract(IBlobPathSource path)
        {
            Dictionary<string, Type> contract = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            contract.Add("BlobTrigger", typeof(string));

            IReadOnlyDictionary<string, Type> contractFromPath = path.CreateBindingDataContract();

            if (contractFromPath != null)
            {
                foreach (KeyValuePair<string, Type> item in contractFromPath)
                {
                    // In case of conflict, binding data from the value type overrides the built-in binding data above.
                    contract[item.Key] = item.Value;
                }
            }

            return contract;
        }

        private static IAsyncObjectToTypeConverter<ICloudBlob> CreateConverter(CloudBlobClient client)
        {
            return new CompositeAsyncObjectToTypeConverter<ICloudBlob>(
                new OutputConverter<ICloudBlob>(new AsyncIdentityConverter<ICloudBlob>()),
                new OutputConverter<string>(new StringToCloudBlobConverter(client)));
        }

        public async Task<ITriggerData> BindAsync(ICloudBlob value, ValueBindingContext context)
        {
            IValueProvider valueProvider = await _argumentBinding.BindAsync(value, context);
            IReadOnlyDictionary<string, object> bindingData = CreateBindingData(value);

            return new TriggerData(valueProvider, bindingData);
        }

        public async Task<ITriggerData> BindAsync(object value, ValueBindingContext context)
        {
            ConversionResult<ICloudBlob> conversionResult = await _converter.TryConvertAsync(value,
                context.CancellationToken);

            if (!conversionResult.Succeeded)
            {
                throw new InvalidOperationException("Unable to convert trigger to ICloudBlob.");
            }

            return await BindAsync(conversionResult.Result, context);
        }

        public IFunctionDefinition CreateFunctionDefinition(IReadOnlyDictionary<string, IBinding> nonTriggerBindings,
            IInvoker invoker, FunctionDescriptor functionDescriptor)
        {
            ITriggeredFunctionBinding<ICloudBlob> functionBinding =
                new TriggeredFunctionBinding<ICloudBlob>(_parameterName, this, nonTriggerBindings);
            ITriggeredFunctionInstanceFactory<ICloudBlob> instanceFactory =
                new TriggeredFunctionInstanceFactory<ICloudBlob>(functionBinding, invoker, functionDescriptor);
            CloudBlobContainer container = _client.GetContainerReference(_path.ContainerNamePattern);
            IListenerFactory listenerFactory = new BlobListenerFactory(functionDescriptor.Id, container, _path,
                instanceFactory);
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
            Dictionary<string, object> bindingData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            bindingData.Add("BlobTrigger", value.GetBlobPath());

            IReadOnlyDictionary<string, object> bindingDataFromPath = _path.CreateBindingData(value.ToBlobPath());

            if (bindingDataFromPath != null)
            {
                foreach (KeyValuePair<string, object> item in bindingDataFromPath)
                {
                    // In case of conflict, binding data from the value type overrides the built-in binding data above.
                    bindingData[item.Key] = item.Value;
                }
            }
            return bindingData;
        }
    }
}
