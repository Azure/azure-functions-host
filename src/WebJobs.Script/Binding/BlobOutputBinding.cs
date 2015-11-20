// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class BlobOutputBinding : OutputBinding
    {
        private readonly BindingTemplate _pathBindingTemplate;

        public BlobOutputBinding(string name, string path) : base(name, "blob")
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

        public override async Task BindAsync(IBinder binder, Stream stream, IReadOnlyDictionary<string, string> bindingData)
        {
            string boundBlobPath = Path;
            if (bindingData != null)
            {
                boundBlobPath = _pathBindingTemplate.Bind(bindingData);
            }

            Stream outStream = binder.Bind<Stream>(new BlobAttribute(boundBlobPath, FileAccess.Write));
            await stream.CopyToAsync(outStream);
        }
    }
}
