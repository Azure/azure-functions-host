// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core
{
    /// <summary>
    /// Represents an abstraction to build a WorkerDescription object
    /// </summary>
    public interface IWorkerDescriptionBuilder
    {
        WorkerDescription Build(string workerConfigPath, string workerDirectory);
    }
}
