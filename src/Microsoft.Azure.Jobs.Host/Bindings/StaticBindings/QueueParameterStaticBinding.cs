using System;
using Newtonsoft.Json;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs
{
    internal class QueueParameterStaticBinding : ParameterStaticBinding
    {
        [JsonIgnore]
        private string _queueName;

        public string QueueName
        {
            get
            {
                return _queueName;
            }
            set
            {
                string name = value.ToLowerInvariant(); // must be lowercase. coerce here to be nice.
                QueueClient.ValidateQueueName(name);
                this._queueName = name;
            }
        }

        public override ParameterRuntimeBinding Bind(IRuntimeBindingInputs inputs)
        {
            return this.BindFromInvokeString(inputs, null);
        }

        public override ParameterRuntimeBinding BindFromInvokeString(IRuntimeBindingInputs inputs, string invokeString)
        {
            string queueName;

            if (string.IsNullOrEmpty(invokeString))
            {
                queueName = _queueName;
            }
            else
            {
                queueName = invokeString;
            }

            return new QueueOutputParameterRuntimeBinding
            {
                Name = Name,
                QueueOutput = new CloudQueueDescriptor
                {
                    AccountConnectionString = inputs.StorageConnectionString,
                    QueueName = queueName
                }
            };
        }

        public override ParameterDescriptor ToParameterDescriptor()
        {
            return new QueueParameterDescriptor
            {
                QueueName = QueueName,
                IsInput = false
            };
        }
    }
}
