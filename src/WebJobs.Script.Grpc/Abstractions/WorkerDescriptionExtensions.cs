// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Abstractions
{
    public static class WorkerDescriptionExtensions
    {
        public static bool IsValid(this WorkerDescription workerDescription)
        {
            if (string.IsNullOrEmpty(workerDescription.Language))
            {
                throw new ArgumentNullException(nameof(workerDescription.Language));
            }
            if (workerDescription.Extensions == null)
            {
                throw new ArgumentNullException(nameof(workerDescription.Extensions));
            }
            if (string.IsNullOrEmpty(workerDescription.DefaultExecutablePath))
            {
                throw new ArgumentNullException(nameof(workerDescription.DefaultExecutablePath));
            }
            if (string.IsNullOrEmpty(workerDescription.DefaultWorkerPath))
            {
                throw new ArgumentNullException(nameof(workerDescription.DefaultWorkerPath));
            }
            return true;
        }
    }
}
