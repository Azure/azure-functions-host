// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.ComponentModel.DataAnnotations;

namespace Microsoft.Azure.WebJobs.Script.OutOfProc.Http
{
    public class HttpWorkerDescription : WorkerDescription
    {
        public override void ApplyDefaultsAndValidate()
        {
            base.ApplyDefaultsAndValidate();
            if (string.IsNullOrEmpty(DefaultExecutablePath))
            {
                throw new ValidationException($"WorkerDescription {nameof(DefaultExecutablePath)} cannot be empty");
            }
        }
    }
}