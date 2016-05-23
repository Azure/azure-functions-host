// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;
using Microsoft.Azure.WebJobs.Host.Bindings.Runtime;
using Microsoft.Azure.WebJobs.Script.Description;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public class ApiHubTableBinding : FunctionBinding
    {
        private readonly BindingTemplate _dataSetNameBindingTemplate;
        private readonly BindingTemplate _tableNameBindingTemplate;
        private readonly BindingTemplate _entityIdBindingTemplate;

        public ApiHubTableBinding(
            ScriptHostConfiguration config, 
            ApiHubTableBindingMetadata metadata, 
            FileAccess access) 
            : base(config, metadata, access)
        {
            Connection = metadata.Connection;
            DataSetName = metadata.DataSetName;
            TableName = metadata.TableName;
            EntityId = metadata.EntityId;
            BindingDirection = metadata.Direction;

            _dataSetNameBindingTemplate = CreateBindingTemplate(DataSetName);
            _tableNameBindingTemplate = CreateBindingTemplate(TableName);
            _entityIdBindingTemplate = CreateBindingTemplate(EntityId);
        }

        public string Connection { get; }

        public string DataSetName { get; }

        public string TableName { get; }

        public string EntityId { get; }

        public BindingDirection BindingDirection { get; }

        public override Collection<CustomAttributeBuilder> GetCustomAttributes(Type parameterType)
        {
            var constructorTypes = new[] { typeof(string) };
            var constructor = typeof(ApiHubTableAttribute).GetConstructor(constructorTypes);
            var constructorArguments = new[] { Connection };
            var namedProperties = new[]
            {
                typeof(ApiHubTableAttribute).GetProperty("DataSetName"),
                typeof(ApiHubTableAttribute).GetProperty("TableName"),
                typeof(ApiHubTableAttribute).GetProperty("EntityId")
            };
            var propertyValues = new[]
            {
                DataSetName,
                TableName,
                EntityId
            };

            return new Collection<CustomAttributeBuilder>()
            {
                new CustomAttributeBuilder(
                    constructor,
                    constructorArguments,
                    namedProperties,
                    propertyValues)
            };
        }

        public override async Task BindAsync(BindingContext context)
        {
            var attribute = new ApiHubTableAttribute(Connection)
            {
                DataSetName = BindAndResolve(DataSetName, _dataSetNameBindingTemplate, context),
                TableName = BindAndResolve(TableName, _tableNameBindingTemplate, context),
                EntityId = BindAndResolve(EntityId, _entityIdBindingTemplate, context)
            };

            var runtimeContext = new RuntimeBindingContext(attribute);

            if (Access == FileAccess.Read && BindingDirection == BindingDirection.In)
            {
                context.Value = await context.Binder.BindAsync<JObject>(runtimeContext);
            }
            else if (Access == FileAccess.Write && BindingDirection == BindingDirection.Out)
            {
                await BindAsyncCollectorAsync<JObject>(context, runtimeContext);
            }
        }

        private static BindingTemplate CreateBindingTemplate(string value)
        {
            return 
                value != null
                    ? BindingTemplate.FromString(value)
                    : null;
        }

        private string BindAndResolve(
            string value, BindingTemplate template, BindingContext context)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            if (context.BindingData == null || template == null)
            {
                return Resolve(value);
            }

            return Resolve(template.Bind(context.BindingData));
        }
    }
}
