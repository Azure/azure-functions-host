// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Abstractions.Description;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public interface ICompilationService
    {
        string Language { get; }

        /// <summary>
        /// Gets a value indicating whether this compilation service persists compilation output to the common file system.
        /// </summary>
        bool PersistsOutput { get; }

        IEnumerable<string> SupportedFileTypes { get; }

        Task<object> GetFunctionCompilationAsync(FunctionMetadata functionMetadata);
    }

    public interface ICompilationService<TCompilation> : ICompilationService where TCompilation : ICompilation
    {
        new Task<TCompilation> GetFunctionCompilationAsync(FunctionMetadata functionMetadata);
    }
}
