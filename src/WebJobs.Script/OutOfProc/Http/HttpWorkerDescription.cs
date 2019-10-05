// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;

namespace Microsoft.Azure.WebJobs.Script.OutOfProc.Http
{
    public class HttpWorkerDescription : WorkerDescription
    {
        public override void ApplyDefaultsAndValidate(string workerDirectory)
        {
            if (workerDirectory == null)
            {
                throw new ArgumentNullException(nameof(workerDirectory));
            }
            Arguments = Arguments ?? new List<string>();

            WorkerDirectory = WorkerDirectory ?? workerDirectory;

            // If DefaultWorkerPath is not set then compute full path for DefaultExecutablePath from scriptRootDir
            if (string.IsNullOrEmpty(DefaultWorkerPath) && !string.IsNullOrEmpty(DefaultExecutablePath) && !Path.IsPathRooted(DefaultExecutablePath))
            {
                DefaultExecutablePath = Path.Combine(workerDirectory, DefaultExecutablePath);
            }

            // If DefaultWorkerPath is set and find full path from scriptRootDir
            if (!string.IsNullOrEmpty(DefaultWorkerPath) && !Path.IsPathRooted(DefaultWorkerPath))
            {
                DefaultWorkerPath = Path.Combine(WorkerDirectory, DefaultWorkerPath);
            }

            if (string.IsNullOrEmpty(DefaultExecutablePath))
            {
                throw new ValidationException($"WorkerDescription {nameof(DefaultExecutablePath)} cannot be empty");
            }
        }
    }
}