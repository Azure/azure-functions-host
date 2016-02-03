// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public class BlobBinding : FunctionBinding
    {
        private readonly BindingTemplate _pathBindingTemplate;

        public BlobBinding(ScriptHostConfiguration config, string name, string path, FileAccess fileAccess, bool isTrigger) : base(config, name, "blob", fileAccess, isTrigger)
        {
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

            Stream blobStream = context.Binder.Bind<Stream>(new BlobAttribute(boundBlobPath, FileAccess));
            if (FileAccess == FileAccess.Write)
            {
                await context.Value.CopyToAsync(blobStream);
            }
            else
            {
                await blobStream.CopyToAsync(context.Value);
            }
        }
    }
}
