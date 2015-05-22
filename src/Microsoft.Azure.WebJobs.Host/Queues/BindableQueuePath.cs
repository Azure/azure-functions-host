// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;

namespace Microsoft.Azure.WebJobs.Host.Queues
{
    /// <summary>
    /// Utility class with factory method to create an instance of a strategy class implementing <see cref="IBindableQueuePath"/> interface.
    /// </summary>
    internal static class BindableQueuePath
    {
        /// <summary>
        /// Factory method detecting parameters in supplied queue name pattern and creating 
        /// an instance of relevant strategy implementing <see cref="IBindableQueuePath"/>.
        /// </summary>
        /// <param name="queueNamePattern">Storage queue name pattern containing optional binding parameters.</param>
        /// <returns>An object implementing <see cref="IBindableQueuePath"/></returns>
        public static IBindableQueuePath Create(string queueNamePattern)
        {
            if (queueNamePattern == null)
            {
                throw new ArgumentNullException("queueNamePattern");
            }

            BindingTemplate template = BindingTemplate.FromString(queueNamePattern);
            
            if (template.ParameterNames.Count() > 0)
            {
                return new ParameterizedQueuePath(template);
            }

            return new BoundQueuePath(queueNamePattern);
        }

        /// <summary>
        /// Helper method to normalize and validate resolved queue name.
        /// </summary>
        /// <param name="queueName">A storage queue name containing no parameters</param>
        /// <returns>Normalized (lower-cased) storage queue name</returns>
        /// <exception cref="System.ArgumentException">If the normalized name is invalid</exception>
        public static string NormalizeAndValidate(string queueName)
        {
            queueName = queueName.ToLowerInvariant(); // must be lowercase. coerce here to be nice.
            QueueClient.ValidateQueueName(queueName);
            return queueName;
        }
    }
}
