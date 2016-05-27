// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;
using Microsoft.Azure.WebJobs.Host.Bindings.Runtime;
using Microsoft.Azure.WebJobs.Script.Description;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public class MobileTableBinding : FunctionBinding
    {
        private readonly BindingDirection _bindingDirection;
        private readonly BindingTemplate _idBindingTemplate;

        public MobileTableBinding(ScriptHostConfiguration config, MobileTableBindingMetadata metadata, FileAccess access) :
            base(config, metadata, access)
        {
            Id = metadata.Id;
            if (!string.IsNullOrEmpty(Id))
            {
                _idBindingTemplate = BindingTemplate.FromString(Id);
            }

            TableName = metadata.TableName;
            MobileAppUri = metadata.Connection;
            ApiKey = metadata.ApiKey;

            _bindingDirection = metadata.Direction;
        }

        public string TableName { get; private set; }

        public string Id { get; private set; }

        public string MobileAppUri { get; private set; }

        public string ApiKey { get; private set; }

        public override Collection<CustomAttributeBuilder> GetCustomAttributes(Type parameterType)
        {
            PropertyInfo[] props = new[]
            {
                typeof(MobileTableAttribute).GetProperty("TableName"),
                typeof(MobileTableAttribute).GetProperty("Id"),
                typeof(MobileTableAttribute).GetProperty("MobileAppUri"),
                typeof(MobileTableAttribute).GetProperty("ApiKey"),
            };

            object[] propValues = new[]
            {
                TableName,
                Id,
                MobileAppUri,
                ApiKey
            };

            ConstructorInfo constructor = typeof(MobileTableAttribute).GetConstructor(System.Type.EmptyTypes);

            return new Collection<CustomAttributeBuilder>
            {
                new CustomAttributeBuilder(constructor, new object[] { }, props, propValues)
            };
        }

        public override async Task BindAsync(BindingContext context)
        {
            string boundId = ResolveBindingTemplate(Id, _idBindingTemplate, context.BindingData);

            MobileTableAttribute attribute = new MobileTableAttribute
            {
                TableName = TableName,
                Id = boundId,
                MobileAppUri = MobileAppUri,
                ApiKey = ApiKey
            };

            RuntimeBindingContext runtimeContext = new RuntimeBindingContext(attribute);

            if (Access == FileAccess.Read && _bindingDirection == BindingDirection.In)
            {
                context.Value = await context.Binder.BindAsync<JObject>(runtimeContext);
            }
            else if (Access == FileAccess.Write && _bindingDirection == BindingDirection.Out)
            {
                await BindAsyncCollectorAsync<JObject>(context, runtimeContext);
            }
        }
    }
}
