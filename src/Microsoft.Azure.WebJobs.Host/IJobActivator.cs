// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Host
{
    /// <summary>Defines an activator that creates an instance of a job type.</summary>
    public interface IJobActivator
    {
        /// <summary>Creates a new instance of a job type.</summary>
        /// <typeparam name="T">The job type.</typeparam>
        /// <returns>A new instance of the job type.</returns>
        T CreateInstance<T>();
    }
}
