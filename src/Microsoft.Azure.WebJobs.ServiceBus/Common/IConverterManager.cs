// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    // General service for converting between types.
    // Expose as a service on config so that multiple extensions can plug. 
    internal interface IConverterManager
    {
        // Get gives precedence to returning an exact match from AddConverter.
        // It also has rules for composing converters. 
        Func<TSrc, TDest> GetConverter<TSrc, TDest>();

        void AddConverter<TSrc, TDest>(Func<TSrc, TDest> converter);
    }

}