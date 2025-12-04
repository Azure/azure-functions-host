// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script;

public class FunctionMetadataOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether a script file is required for function metadata. Some workers
    /// (such as HTTP workers) do not require a script file to be present.
    /// </summary>
    public bool SkipScriptFileValidation { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether to validate that the worker runtime matches the language
    /// in the function metadata. Some workers (such as HTTP workers) do not require this validation.
    /// </summary>
    public bool SkipRuntimeValidation { get; set; } = false;
}
