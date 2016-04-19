// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings.Runtime;
using Microsoft.Azure.WebJobs.Script.Description;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public class MobileTableBinding : FunctionBinding
    {
        private readonly BindingDirection _bindingDirection;

        public MobileTableBinding(ScriptHostConfiguration config, MobileTableBindingMetadata metadata, FileAccess access) :
            base(config, metadata, access)
        {
            TableName = metadata.TableName;
            Id = metadata.Id;
            MobileAppUri = metadata.Connection;
            ApiKey = metadata.ApiKey;

            _bindingDirection = metadata.Direction;
        }

        public string TableName { get; private set; }

        public string Id { get; private set; }

        public string MobileAppUri { get; private set; }

        public string ApiKey { get; private set; }

        public override Collection<CustomAttributeBuilder> GetCustomAttributes()
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
            MobileTableAttribute attribute = new MobileTableAttribute
            {
                TableName = TableName,
                Id = Id,
                MobileAppUri = MobileAppUri,
                ApiKey = ApiKey
            };

            RuntimeBindingContext runtimeContext = new RuntimeBindingContext(attribute);

            if (Access == FileAccess.Read && _bindingDirection == BindingDirection.In)
            {
                JObject input = await context.Binder.BindAsync<JObject>(runtimeContext);
                if (input != null)
                {
                    byte[] byteArray = Encoding.UTF8.GetBytes(input.ToString());
                    using (MemoryStream stream = new MemoryStream(byteArray))
                    {
                        stream.CopyTo(context.Value);
                    }
                }
            }
            else if (Access == FileAccess.Write && _bindingDirection == BindingDirection.Out)
            {
                await BindAsyncCollectorAsync<JObject>(context.Value, context.Binder, runtimeContext);
            }
        }
    }
}
