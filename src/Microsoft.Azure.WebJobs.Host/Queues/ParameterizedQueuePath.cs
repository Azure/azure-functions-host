// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;

namespace Microsoft.Azure.WebJobs.Host.Queues
{
    /// <summary>
    /// Implementation of <see cref="IBindableQueuePath"/> strategy for paths 
    /// containing one or more parameters.
    /// </summary>
    internal class ParameterizedQueuePath : IBindableQueuePath
    {
        private readonly BindingTemplate _template;

        public ParameterizedQueuePath(BindingTemplate template)
        {
            Debug.Assert(template != null, "template must not be null");
            Debug.Assert(template.ParameterNames.Count() > 0, "template must contain one or more parameters");

            _template = template;
        }

        public string QueueNamePattern
        {
            get { return _template.Pattern; }
        }

        public bool IsBound
        {
            get { return false; }
        }

        public IEnumerable<string> ParameterNames
        {
            get { return _template.ParameterNames; }
        }

        public string Bind(IReadOnlyDictionary<string, object> bindingData)
        {
            if (bindingData == null)
            {
                throw new ArgumentNullException("bindingData");
            }

            IReadOnlyDictionary<string, string> parameters = BindingDataPath.ConvertParameters(bindingData);
            string queueName = _template.Bind(parameters);
            return BindableQueuePath.NormalizeAndValidate(queueName);
        }
    }
}
