// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Azure.Cosmos;

namespace Microsoft.Azure.WebJobs.Script.Tests.CosmosDB;

internal static class CosmosDBEmulator
{
    // Keep these tests independent of provisioned accounts in environment variables or user test settings.
    [SuppressMessage("Microsoft.Security", "CS002:SecretInNextLine", Justification = "Well-known public emulator key.")]
    internal const string ConnectionString = "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;";

    internal static CosmosClient CreateClient() => new(ConnectionString, new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway });
}
