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

            public override void OnPostAction()
            {
                if (this.Result != null)
                {
                    string json = JsonCustom.SerializeObject(this.Result);
                    CloudQueueMessage msg = new CloudQueueMessage(json);
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

            return new QueueResult { Queue = this.QueueOutput.GetQueue() };
        }

        public override string ConvertToInvokeString()
        {
            throw new NotImplementedException();
        }

        public override string ToString()
        {
            return string.Format("Output to queue: {0}", QueueOutput.QueueName);
        }
    }
}