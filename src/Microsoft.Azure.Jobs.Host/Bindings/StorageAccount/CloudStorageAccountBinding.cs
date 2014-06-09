using System;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.Bindings.StorageAccount
{
    internal class CloudStorageAccountBinding : IBinding
    {
        private static readonly IObjectToTypeConverter<CloudStorageAccount> _converter =
            new CompositeObjectToTypeConverter<CloudStorageAccount>(
                new OutputConverter<CloudStorageAccount>(new IdentityConverter<CloudStorageAccount>()),
                new OutputConverter<string>(new StringToCloudStorageAccountConverter()));

        private readonly string _parameterName;
        private readonly string _accountName;

        public CloudStorageAccountBinding(string parameterName, CloudStorageAccount account)
        {
            _parameterName = parameterName;
            _accountName = StorageClient.GetAccountName(account);
        }

        public bool FromAttribute
        {
            get { return false; }
        }

        private IValueProvider Bind(CloudStorageAccount account, ArgumentBindingContext context)
        {
            return new CloudStorageAccountValueProvider(account);
        }

        public IValueProvider Bind(object value, ArgumentBindingContext context)
        {
            CloudStorageAccount account = null;

            if (!_converter.TryConvert(value, out account))
            {
                throw new InvalidOperationException("Unable to convert value to CloudStorageAccount.");
            }

            return Bind(account, context);
        }

        public IValueProvider Bind(BindingContext context)
        {
            return Bind(context.StorageAccount, context);
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return new CloudStorageAccountParameterDescriptor
            {
                Name = _parameterName,
                AccountName = _accountName
            };
        }
    }
}
