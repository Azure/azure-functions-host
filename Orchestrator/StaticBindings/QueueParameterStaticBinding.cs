using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure;
using Newtonsoft.Json;
using RunnerHost;
using RunnerInterfaces;

namespace Orchestrator
{
    public class QueueParameterStaticBinding : ParameterStaticBinding
    {
        // Is this enqueue or dequeue?
        public bool IsInput { get; set; }

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
                string name = value.ToLower(); // must be lowercase. coerce here to be nice.
                Utility.ValidateQueueName(name);
                this._queueName = name;
            }
        }        

        public override ParameterRuntimeBinding Bind(RuntimeBindingInputs inputs)
        {
            if (this.IsInput)
            {
                string payload = inputs._queueMessageInput.AsString;
                return new LiteralObjectParameterRuntimeBinding { LiteralJson = payload };
            }
            else
            {
                // Will set on out parameter.
                return new QueueOutputParameterRuntimeBinding
                {
                    QueueOutput = new CloudQueueDescriptor
                    {
                        AccountConnectionString = Utility.GetConnectionString(inputs._account),
                        QueueName =  this.QueueName
                    }
                };
            }
        }

        public override ParameterRuntimeBinding BindFromInvokeString(CloudStorageAccount account, string invokeString)
        {
            if (this.IsInput)
            {
                return new LiteralObjectParameterRuntimeBinding { LiteralJson = invokeString };
            }
            throw new NotImplementedException();
        }

        public override string Description
        {
            get {
                if (this.IsInput)
                {
                    return string.Format("dequeue from '{0}'", this.QueueName);
                }
                else
                {
                    return string.Format("enqueue to '{0}'", this.QueueName);
                }
            }
        }
    }
}
