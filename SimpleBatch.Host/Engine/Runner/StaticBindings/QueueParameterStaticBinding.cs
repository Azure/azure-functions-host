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

        public override ParameterRuntimeBinding Bind(IRuntimeBindingInputs inputs)
        {
            string payload = null;
            if (this.IsInput)
            {
                var inputQueueMsg = (ITriggerNewQueueMessage)inputs;

                QueueCausalityHelper qcm = new QueueCausalityHelper();
                payload = qcm.DecodePayload(inputQueueMsg.QueueMessageInput);
            }
            return this.BindFromInvokeString(inputs, payload);
        }

        public override ParameterRuntimeBinding BindFromInvokeString(IRuntimeBindingInputs inputs, string invokeString)
        {
            if (this.IsInput)
            {
                return new LiteralObjectParameterRuntimeBinding { LiteralJson = invokeString };
            }
            else
            {
                // invokeString is ignored. 
                // Will set on out parameter.
                return new QueueOutputParameterRuntimeBinding
                {
                    QueueOutput = new CloudQueueDescriptor
                    {
                        AccountConnectionString = inputs.AccountConnectionString,
                        QueueName = this.QueueName
                    }
                };
            }
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
