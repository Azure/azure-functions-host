// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.Tests;

public class TestDiagnosticEventRepositoryFactory : IDiagnosticEventRepositoryFactory
{
    private IDiagnosticEventRepository _repository;

    public TestDiagnosticEventRepositoryFactory(IDiagnosticEventRepository repository)
    {
        _repository = repository;
    }

    public IDiagnosticEventRepository Create()
    {
        return _repository;
    }
}