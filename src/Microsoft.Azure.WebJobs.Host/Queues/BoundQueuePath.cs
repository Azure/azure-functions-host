// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Azure.WebJobs.Host.Queues
{
    /// <summary>
    /// Bindable queue path strategy implementation for "degenerate" bindable patterns, 
    /// i.e. containing no parameters.
    /// </summary>
    internal class BoundQueuePath : IBindableQueuePath
    {
        private readonly string _queueNamePattern;

        public BoundQueuePath(string queueNamePattern)
        {
            _queueNamePattern = BindableQueuePath.NormalizeAndValidate(queueNamePattern);
        }

        public string QueueNamePattern
        {
            get { return _queueNamePattern; }
        }

        public bool IsBound
        {
            get { return true; }
        }

        public IEnumerable<string> ParameterNames
        {
            get { return Enumerable.Empty<string>(); }
        }

        public string Bind(IReadOnlyDictionary<string, object> bindingData)
        {
            return QueueNamePattern;
        }
    }
}
