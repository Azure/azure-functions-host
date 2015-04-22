// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    internal static class ValueProviderDisposable
    {
        public static IDisposable Create(IReadOnlyDictionary<string, IValueProvider> parameters)
        {
            List<IDisposable> disposableItems = new List<IDisposable>();

            foreach (KeyValuePair<string, IValueProvider> item in parameters)
            {
                IDisposable disposableItem = item.Value as IDisposable;

                if (disposableItem != null)
                {
                    disposableItems.Add(disposableItem);
                }
            }

            if (disposableItems.Count == 0)
            {
                return null;
            }
            else
            {
                return new CompositeDisposable(disposableItems);
            }
        }
    }
}
