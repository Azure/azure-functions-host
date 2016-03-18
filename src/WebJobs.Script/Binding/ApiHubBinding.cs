// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.ApiHub;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;
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
                throw new ArgumentException("The Apihub path cannot be null or empty.");
            }

            KeyName = apiHubBindingMetadata.Key;
            Path = apiHubBindingMetadata.Path;
            _pathBindingTemplate = BindingTemplate.FromString(Path);
        }

        public override bool HasBindingParameters
        {
            get
            {
                return _pathBindingTemplate.ParameterNames.Any();
            }
        }

        public string KeyName { get; private set; }

        public string Path { get; private set; }

        public override Collection<CustomAttributeBuilder> GetCustomAttributes()
        {
            var constructorTypes = new Type[] { typeof(string), typeof(string), typeof(FileAccess) };
            var constructorArguments = new object[] { KeyName, Path, FileAccess.Read };

            return new Collection<CustomAttributeBuilder>
            {
                new CustomAttributeBuilder(typeof(ApiHubFileAttribute).GetConstructor(constructorTypes), constructorArguments)
            };
        }

        public override Task BindAsync(BindingContext context)
        {
            return Task.FromResult(0);
        }
    }
}
