// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    /// <summary>
    /// XUnit collection definition to disable parallelization. This is used for tests which
    /// set global state and cannot run with other tests. A primary example is any test using
    /// <see cref="TestScopedEnvironmentVariable" />.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class DisableParallelizationCollection
    {
        public const string Name = "DisableParallelization";
    }
}