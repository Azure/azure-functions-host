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

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public class ApiHubBinding : FunctionBinding
    {
        private readonly BindingTemplate _pathBindingTemplate;

        public ApiHubBinding(ScriptHostConfiguration config, ApiHubBindingMetadata apiHubBindingMetadata, FileAccess access) : base(config, apiHubBindingMetadata, access)
        {
            if (apiHubBindingMetadata == null)
            {
                throw new ArgumentNullException("apiHubBindingMetadata");
            }

            if (string.IsNullOrEmpty(apiHubBindingMetadata.Path))
            {
                throw new ArgumentException("The ApiHubFile path cannot be null or empty.");
            }

            Key = apiHubBindingMetadata.Key;
            Path = apiHubBindingMetadata.Path;
            _pathBindingTemplate = BindingTemplate.FromString(Path);
        }

        public string Key { get; private set; }

        public string Path { get; private set; }

        public override Collection<CustomAttributeBuilder> GetCustomAttributes(Type parameterType)
        {
            Collection<CustomAttributeBuilder> attributes = new Collection<CustomAttributeBuilder>();

            var constructorTypes = new Type[] { typeof(string), typeof(string), typeof(FileAccess) };
            var constructorArguments = new object[] { Key, Path, FileAccess.Read };

            var attribute = new CustomAttributeBuilder(typeof(ApiHubFileAttribute).GetConstructor(constructorTypes), constructorArguments);

            attributes.Add(attribute);

            return attributes;
        }

        public override async Task BindAsync(BindingContext context)
        {
            string boundBlobPath = Path;
            if (context.BindingData != null)
            {
                boundBlobPath = _pathBindingTemplate.Bind(context.BindingData);
            }

            boundBlobPath = Resolve(boundBlobPath);

            var attribute = new ApiHubFileAttribute(Key, boundBlobPath, Access);

            RuntimeBindingContext runtimeContext = new RuntimeBindingContext(attribute);
            await BindStreamAsync(context.Value, Access, context.Binder, runtimeContext);
        }
    }
}
