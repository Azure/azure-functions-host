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
    public class BlobBinding : FunctionBinding
    {
        private readonly BindingTemplate _pathBindingTemplate;

        public BlobBinding(ScriptHostConfiguration config, string name, string path, FileAccess access, bool isTrigger) : base(config, name, "blob", access, isTrigger)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("The blob path cannot be null or empty.");
            }

            Path = path;
            _pathBindingTemplate = BindingTemplate.FromString(Path);
        }

        public string Path { get; private set; }

        public override bool HasBindingParameters
        {
            get
            {
                return _pathBindingTemplate.ParameterNames.Any();
            }
        }

        public override async Task BindAsync(BindingContext context)
        {
            string boundBlobPath = Path;
            if (context.BindingData != null)
            {
                boundBlobPath = _pathBindingTemplate.Bind(context.BindingData);
            }

            boundBlobPath = Resolve(boundBlobPath);

            // TODO: Need to handle Stream conversions properly
            Stream valueStream = context.Value as Stream;

            Stream blobStream = context.Binder.Bind<Stream>(new BlobAttribute(boundBlobPath, Access));
            if (Access == FileAccess.Write)
            {
                await valueStream.CopyToAsync(blobStream);
            }
            else
            {
                await blobStream.CopyToAsync(valueStream);
            }
        }

        public override CustomAttributeBuilder GetCustomAttribute()
        {
            var constructorTypes = new Type[] { typeof(string), typeof(FileAccess) };
            var constructorArguments = new object[] { Path, Access };

            return new CustomAttributeBuilder(typeof(BlobAttribute).GetConstructor(constructorTypes), constructorArguments);
        }
    }
}
