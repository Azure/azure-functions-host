// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public class EasyTableBinding : FunctionBinding
    {
        private readonly BindingDirection _bindingDirection;

        public EasyTableBinding(ScriptHostConfiguration config, string name, string tableName, string id, FileAccess access, BindingDirection direction) :
            base(config, name, "easytable", access, false)
        {
            this.TableName = tableName;
            this.Id = id;
            _bindingDirection = direction;
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

        public override CustomAttributeBuilder GetCustomAttribute()
        {
            PropertyInfo[] props = new[]
            {
                typeof(EasyTableAttribute).GetProperty("TableName"),
                typeof(EasyTableAttribute).GetProperty("Id")
            };

            object[] propValues = new[]
            {
                this.TableName,
                this.Id
            };

            ConstructorInfo constructor = typeof(EasyTableAttribute).GetConstructor(System.Type.EmptyTypes);

            return new CustomAttributeBuilder(constructor, new object[] { }, props, propValues);
        }

        public override async Task BindAsync(BindingContext context)
        {
            EasyTableAttribute attribute = new EasyTableAttribute
            {
                TableName = this.TableName,
                Id = this.Id
            };

            // Only output bindings are supported.
            if (this.Access == FileAccess.Write && _bindingDirection == BindingDirection.Out)
            {
                IAsyncCollector<JObject> collector = context.Binder.Bind<IAsyncCollector<JObject>>(attribute);
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
