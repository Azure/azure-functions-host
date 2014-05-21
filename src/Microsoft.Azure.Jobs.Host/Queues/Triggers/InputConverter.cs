using System;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Triggers
{
    internal class InputConverter<TOutput> : ITypeToObjectConverter<CloudQueueMessage>
    {
        readonly IConverter<CloudQueueMessage, TOutput> _innerConverter;

        public InputConverter(IConverter<CloudQueueMessage, TOutput> innerConverter)
        {
            _innerConverter = innerConverter;
        }

        public bool CanConvert(Type outputType)
        {
            return typeof(TOutput) == outputType;
        }

        public object Convert(CloudQueueMessage input)
        {
            return _innerConverter.Convert(input);
        }
    }
}
