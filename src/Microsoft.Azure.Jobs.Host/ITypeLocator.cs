// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.Jobs
{
    /// <summary>Defines a locator that identifies types that may contain functions for <see cref="JobHost"/> to execute.</summary>
    public interface ITypeLocator
    {        
        /// <summary>Retrieves types that may contain functions for <see cref="JobHost"/> to execute.</summary>
        /// <returns>Types that may contain functions for <see cref="JobHost"/> to execute.</returns>
        IReadOnlyList<Type> GetTypes();
    }
}
