// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public class QueueBinding : FunctionBinding
    {
        private readonly BindingTemplate _queueNameBindingTemplate;

        public QueueBinding(ScriptHostConfiguration config, string name, string queueName, FileAccess access, bool isTrigger) : base(config, name, "queue", access, isTrigger)
        {   
            QueueName = queueName;
            _queueNameBindingTemplate = BindingTemplate.FromString(QueueName);
        }

        public string QueueName { get; private set; }

        public override bool HasBindingParameters
        {
            get
            {
                return _queueNameBindingTemplate.ParameterNames.Any();
            }
        }

        public override CustomAttributeBuilder GetCustomAttribute()
        {
            var constructorTypes = new Type[] { typeof(string) };
            var constructorArguments = new object[] { QueueName };

            return new CustomAttributeBuilder(typeof(QueueAttribute).GetConstructor(constructorTypes), constructorArguments);
        }

        public override async Task BindAsync(BindingContext context)
        {
            string boundQueueName = QueueName;
            if (context.BindingData != null)
            {
                boundQueueName = _queueNameBindingTemplate.Bind(context.BindingData);
            }

            boundQueueName = Resolve(boundQueueName);
            
            // TODO: Need to handle Stream conversions properly
            Stream valueStream = context.Value as Stream;

            // only an output binding is supported
            IAsyncCollector<byte[]> collector = context.Binder.Bind<IAsyncCollector<byte[]>>(new QueueAttribute(boundQueueName));
            byte[] bytes;
            using (MemoryStream ms = new MemoryStream())
            {
                valueStream.CopyTo(ms);
                bytes = ms.ToArray();
            }
            await collector.AddAsync(bytes);
        }
    }
}
