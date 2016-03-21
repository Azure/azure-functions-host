// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    internal class NotificationHubBinding : FunctionBinding
    {
        private BindingDirection _bindingDirection;

        public NotificationHubBinding(ScriptHostConfiguration config, string name, string tagExpression, FileAccess access, BindingDirection direction) :
            base(config, name, BindingType.NotificationHub, access, false)
        {
            TagExpression = tagExpression;
            _bindingDirection = direction;
        }

        public string TagExpression { get; private set; }

        public override bool HasBindingParameters
        {
            get
            {
                return false;
            }
        }

        public override CustomAttributeBuilder GetCustomAttribute()
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

            return new CustomAttributeBuilder(constructor, new object[] { }, props, propValues);
        }

        public override async Task BindAsync(BindingContext context)
        {
            NotificationHubAttribute attribute = new NotificationHubAttribute
            {
                TagExpression = TagExpression
            };

            // Only output bindings are supported.
            if (Access == FileAccess.Write && _bindingDirection == BindingDirection.Out)
            {
                IAsyncCollector<string> collector = context.Binder.Bind<IAsyncCollector<string>>(attribute);
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
