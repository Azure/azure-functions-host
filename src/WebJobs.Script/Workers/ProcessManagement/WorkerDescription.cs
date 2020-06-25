﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    public abstract class WorkerDescription
    {
        // Can be replaced for testing purposes
        internal Func<string, bool> FileExists { get; set; } = File.Exists;

        /// <summary>
        /// Gets or sets the default executable path.
        /// </summary>
        public string DefaultExecutablePath { get; set; }

        /// <summary>
        /// Gets or sets the default path to the worker
        /// </summary>
        public string DefaultWorkerPath { get; set; }

        /// <summary>
        /// Gets or sets the default base directory for the worker
        /// </summary>
        public string WorkerDirectory { get; set; }

        /// <summary>
        /// Gets or sets the command line args to pass to the worker. Will be appended after DefaultExecutablePath but before DefaultWorkerPath
        /// </summary>
        public IList<string> Arguments { get; set; }

        /// <summary>
        /// Gets or sets the command line args to pass to the worker. Will be appended after DefaultWorkerPath
        /// </summary>
        public IList<string> WorkerArguments { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to use stderror stream for all console logs. Default false.
        /// If set to false, Logs from stderr are written with info level by default. If Message contains keywords: error, servere then logs are written with error level.
        /// If set to true, Logs from stdout stream will be written with info level and Logs from stderr steam will be written with error level
        /// </summary>
        public abstract bool UseStdErrorStreamForErrorsOnly { get; set; }

        public abstract void ApplyDefaultsAndValidate(string workerDirectory, ILogger logger);

        internal void ThrowIfFileNotExists(string inputFile, string paramName)
        {
            if (inputFile == null)
            {
                return;
            }
            if (!FileExists(inputFile))
            {
                throw new FileNotFoundException($"File {paramName}: {inputFile} does not exist.");
            }
        }

        internal void ExpandEnvironmentVariables()
        {
            if (DefaultWorkerPath != null)
            {
                DefaultWorkerPath = Environment.ExpandEnvironmentVariables(DefaultWorkerPath);
            }
            if (DefaultExecutablePath != null)
            {
                DefaultExecutablePath = Environment.ExpandEnvironmentVariables(DefaultExecutablePath);
            }
        }
    }
}