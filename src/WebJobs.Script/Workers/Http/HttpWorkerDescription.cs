// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers.Http
{
    public class HttpWorkerDescription : WorkerDescription
    {
        /// <summary>
        /// Gets or sets WorkingDirectory for the process: DefaultExecutablePath
        /// </summary>
        public string WorkingDirectory { get; set; }

        public override bool UseStdErrorStreamForErrorsOnly { get; set; } = true;

        public override void ApplyDefaultsAndValidate(string inputWorkerDirectory, ILogger logger)
        {
            if (inputWorkerDirectory == null)
            {
                throw new ArgumentNullException(nameof(inputWorkerDirectory));
            }
            Arguments = Arguments ?? new List<string>();
            WorkerArguments = WorkerArguments ?? new List<string>();

            if (string.IsNullOrEmpty(WorkerDirectory))
            {
                WorkerDirectory = inputWorkerDirectory;
            }
            else
            {
                if (!Path.IsPathRooted(WorkerDirectory))
                {
                    WorkerDirectory = Path.Combine(inputWorkerDirectory, WorkerDirectory);
                }
            }

            ExpandEnvironmentVariables();

            // If DefaultWorkerPath is not set then compute full path for DefaultExecutablePath from WorkingDirectory and check if DefaultExecutablePath exists
            // Empty DefaultWorkerPath or empty Arguments indicates DefaultExecutablePath is either a runtime on the system path or a file relative to WorkingDirectory.
            // No need to find full path for DefaultWorkerPath as WorkerDirectory will be set when launching the worker process.
            // DefaultWorkerPath can be specified as part of the arguments list
            if (!string.IsNullOrEmpty(DefaultExecutablePath) && !Path.IsPathRooted(DefaultExecutablePath))
            {
                var fullExePath = Path.Combine(WorkerDirectory, DefaultExecutablePath);
                // Override DefaultExecutablePath only if the file exists. If file does not exist assume, this is a runtime available on system path
                if (FileExists(fullExePath))
                {
                    DefaultExecutablePath = fullExePath;
                }
            }

            if (string.IsNullOrEmpty(DefaultExecutablePath))
            {
                throw new ValidationException($"WorkerDescription {nameof(DefaultExecutablePath)} cannot be empty");
            }
        }
    }
}