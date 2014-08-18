// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#if PUBLICPROTOCOL
namespace Microsoft.Azure.WebJobs.Protocols
#else
namespace Microsoft.Azure.WebJobs.Host.Protocols
#endif
{
    /// <summary>Represents a function parameter log for a table parameter.</summary>
    [JsonTypeName("Table")]
#if PUBLICPROTOCOL
    public class TableParameterLog : ParameterLog
#else
    internal class TableParameterLog : ParameterLog
#endif
    {
        /// <summary>Gets or sets the number of entities updated.</summary>
        public int EntitiesUpdated { get; set; }
    }
}
