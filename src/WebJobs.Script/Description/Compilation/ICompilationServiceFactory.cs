// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public interface ICompilationServiceFactory<TCompilationService, TMetadata> where TCompilationService : ICompilationService
    {
        ImmutableArray<string> SupportedLanguages { get; }

        TCompilationService CreateService(string language, TMetadata metadata);
    }
}
