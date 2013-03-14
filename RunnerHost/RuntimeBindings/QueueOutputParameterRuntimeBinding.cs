using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;
using SimpleBatch;

namespace RunnerHost
{

    // On output, the object payload gets queued 
    public class QueueOutputParameterRuntimeBinding : ParameterRuntimeBinding
    {
        public CloudQueueDescriptor QueueOutput { get; set; }

        private class QueueResult : BindResult
        {
            public CloudQueue Queue;
            public Guid thisFunction;

            public override void OnPostAction()
            {
                if (this.Result != null)
                {
                    QueueCausalityHelper qcm = new QueueCausalityHelper();
                    CloudQueueMessage msg = qcm.EncodePayload(thisFunction, this.Result);
                    
                    // Beware, as soon as this is added, 
                    // another worker can pick up the message and start running. 
                    this.Queue.AddMessage(msg);
                }
            }
        }

        public override BindResult Bind(IConfiguration config, IBinderEx bindingContext, ParameterInfo targetParameter)
        {
            if (!targetParameter.IsOut)
            {
                var msg = string.Format("[QueueOutput] is only valid on 'out' parameters. Can't use on '{0}'", targetParameter);
                throw new InvalidOperationException(msg);
            }

            return new QueueResult 
            { 
                thisFunction = bindingContext.FunctionInstanceGuid,
                Queue = this.QueueOutput.GetQueue() 
            };
        }

        public override string ConvertToInvokeString()
        {
            return "[set on output]"; // ignored for output parameters anyways.
        }

        public override string ToString()
        {
            return string.Format("Output to queue: {0}", QueueOutput.QueueName);
        }
    }
}