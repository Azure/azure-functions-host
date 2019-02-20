// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.ComponentModel.DataAnnotations;

namespace Microsoft.Azure.WebJobs.Script.Abstractions
{
    public static class WorkerDescriptionExtensions
    {
        public static void Validate(this WorkerDescription workerDescription)
        {
            if (string.IsNullOrEmpty(workerDescription.Language))
            {
                throw new ValidationException($"WorkerDescription {nameof(workerDescription.Language)} cannot be empty");
            }
            if (workerDescription.Extensions == null)
            {
                throw new ValidationException($"WorkerDescription {nameof(workerDescription.Extensions)} cannot be null");
            }
            if (string.IsNullOrEmpty(workerDescription.DefaultExecutablePath))
            {
                throw new ValidationException($"WorkerDescription {nameof(workerDescription.DefaultExecutablePath)} cannot be empty");
            }
        }
    }
}
