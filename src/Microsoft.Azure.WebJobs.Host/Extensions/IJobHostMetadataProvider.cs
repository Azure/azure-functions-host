// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Host
{
    /// <summary>
    /// Tooling interface for script 
    /// </summary>
    public interface IJobHostMetadataProvider
    {
        /// <summary>
        /// "Blob" --> typeof(BlobAttribute)
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        Type GetAttributeTypeFromName(string name);

        /// <summary>
        /// Create an attribute from the metadata.
        /// (using AttributeCloner). 
        /// </summary>
        /// <param name="attributeType"></param>
        /// <param name="metadata"></param>
        /// <returns></returns>
        Attribute GetAttribute(Type attributeType, JObject metadata);

        /// <summary>
        /// Get a 'default type' that can be used in scripting scenarios.
        /// This is biased to returning JObject, Streams, and BCL types 
        /// which can be converted to a loosely typed object in script languages. 
        /// </summary>
        /// <param name="attribute"></param>
        /// <param name="access"></param>
        /// <param name="requestedType"></param>
        /// <returns></returns>
        Type GetDefaultType(
                Attribute attribute,
                FileAccess access, // direction In, Out, In/Out
                Type requestedType); // combination of Cardinality and DataType 

        /// <summary>
        /// Attempt to resolve the specified reference assembly.
        /// </summary>
        /// <remarks>
        /// This allows an extension to support "built in" assemblies for .NET functions so
        /// user code can easily reference them.
        /// </remarks>
        /// <param name="assemblyName">The name of the assembly to resolve.</param>
        /// <param name="assembly">assembly that the name is resolved to</param>
        /// <returns>True with a non-null assembly if we were able to resolve. Else false and null assembly</returns>
        bool TryResolveAssembly(string assemblyName, out Assembly assembly);

        /// <summary>
        /// For diagnostics, dump the registered bindings and converters.
        /// </summary>
        /// <param name="output"></param>
        void DebugDumpGraph(TextWriter output);
    }
}
