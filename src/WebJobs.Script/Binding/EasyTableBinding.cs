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
    public class EasyTableBinding : FunctionBinding
    {
        private readonly BindingDirection _bindingDirection;

        public EasyTableBinding(ScriptHostConfiguration config, EasyTableBindingMetadata metadata, FileAccess access) :
            base(config, metadata, access)
        {
            TableName = metadata.TableName;
            Id = metadata.Id;
            _bindingDirection = metadata.Direction;
        }

        public string TableName { get; private set; }

        public string Id { get; private set; }

        public override bool HasBindingParameters
        {
            get
            {
                return false;
            }
        }

        public override Collection<CustomAttributeBuilder> GetCustomAttributes()
        {
            PropertyInfo[] props = new[]
            {
                typeof(EasyTableAttribute).GetProperty("TableName"),
                typeof(EasyTableAttribute).GetProperty("Id")
            };

            object[] propValues = new[]
            {
                TableName,
                Id
            };

            ConstructorInfo constructor = typeof(EasyTableAttribute).GetConstructor(System.Type.EmptyTypes);

            return new Collection<CustomAttributeBuilder>
            {
                new CustomAttributeBuilder(constructor, new object[] { }, props, propValues)
            };
        }

        public override async Task BindAsync(BindingContext context)
        {
            // Only output bindings are supported.
            if (Access == FileAccess.Write && _bindingDirection == BindingDirection.Out)
            {
                EasyTableAttribute attribute = new EasyTableAttribute
                {
                    TableName = TableName,
                    Id = Id
                };

                RuntimeBindingContext runtimeContext = new RuntimeBindingContext(attribute);
                IAsyncCollector<JObject> collector = await context.Binder.BindAsync<IAsyncCollector<JObject>>(runtimeContext);
                byte[] bytes;
                using (MemoryStream ms = new MemoryStream())
                {
                    context.Value.CopyTo(ms);
                    bytes = ms.ToArray();
                }
                JObject entity = JObject.Parse(Encoding.UTF8.GetString(bytes));
                await collector.AddAsync(entity);
            }
        }
    }
}
