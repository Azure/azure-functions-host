// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings.Runtime;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    internal class NotificationHubBinding : FunctionBinding
    {
        private BindingDirection _bindingDirection;

        public NotificationHubBinding(ScriptHostConfiguration config, NotificationHubBindingMetadata metadata, FileAccess access) :
            base(config, metadata, access)
        {
            TagExpression = metadata.TagExpression;
            _bindingDirection = metadata.Direction;
        }

        public string TagExpression { get; private set; }

        public override bool HasBindingParameters
        {
            get
            {
                return false;
            }
        }

        public override Collection<CustomAttributeBuilder> GetCustomAttributes()
        {
            Type attributeType = typeof(NotificationHubAttribute);
            PropertyInfo[] props = new[]
            {
                attributeType.GetProperty("TagExpression")
            };

            object[] propValues = new object[]
            {
                TagExpression
            };

            ConstructorInfo constructor = attributeType.GetConstructor(System.Type.EmptyTypes);
            return new Collection<CustomAttributeBuilder>()
            {
                new CustomAttributeBuilder(constructor, new object[] { }, props, propValues)
            };
        }

        public override async Task BindAsync(BindingContext context)
        {
            // Only output bindings are supported.
            if (Access == FileAccess.Write && _bindingDirection == BindingDirection.Out)
            {
                NotificationHubAttribute attribute = new NotificationHubAttribute
                {
                    TagExpression = TagExpression
                };

                RuntimeBindingContext runtimeContext = new RuntimeBindingContext(attribute);
                IAsyncCollector<string> collector = await context.Binder.BindAsync<IAsyncCollector<string>>(runtimeContext);
                byte[] bytes;
                using (MemoryStream ms = new MemoryStream())
                {
                    context.Value.CopyTo(ms);
                    bytes = ms.ToArray();
                }
                var inputString = Encoding.UTF8.GetString(bytes);
                //Only supports valid JSON string
                await collector.AddAsync(inputString);
            }
        }
    }
}
