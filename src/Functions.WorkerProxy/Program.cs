// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Azure.Functions.WorkerProxy;
using Microsoft.Extensions.Options;

try
{
    WebApplication app = WorkerProxyApplication.Build(args);
    await app.RunAsync();

    return 0;
}
catch (OptionsValidationException exception)
{
    await Console.Error.WriteLineAsync(string.Join(Environment.NewLine, exception.Failures));

    return 1;
}
catch (FormatException exception)
{
    await Console.Error.WriteLineAsync(exception.Message);

    return 1;
}
catch (InvalidOperationException exception) when (IsConfigurationBindingFailure(exception))
{
    await Console.Error.WriteLineAsync(exception.Message);

    return 1;
}

static bool IsConfigurationBindingFailure(InvalidOperationException exception)
{
    return exception.InnerException is FormatException or OverflowException;
}
