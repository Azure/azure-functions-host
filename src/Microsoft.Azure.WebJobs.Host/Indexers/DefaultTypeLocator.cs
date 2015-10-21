// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Microsoft.Azure.WebJobs.Host.Indexers
{
    // Default policy for locating types. 
    internal class DefaultTypeLocator : ITypeLocator
    {
        private static readonly string WebJobsAssemblyName = typeof(TableAttribute).Assembly.GetName().Name;

        private readonly TextWriter _log;
        private readonly IExtensionRegistry _extensions;

        public DefaultTypeLocator(TextWriter log, IExtensionRegistry extensions)
        {
            if (log == null)
            {
                throw new ArgumentNullException("log");
            }
            if (extensions == null)
            {
                throw new ArgumentNullException("extensions");
            }

            _log = log;
            _extensions = extensions;
        }

        // Helper to filter out assemblies that don't reference the SDK or
        // binding extension assemblies (i.e. possible sources of binding attributes, etc.)
        private static bool AssemblyReferencesSdkOrExtension(Assembly assembly, IEnumerable<Assembly> extensionAssemblies)
        {
            // Don't index methods in our assemblies.
            if (typeof(DefaultTypeLocator).Assembly == assembly)
            {
                return false;
            }

            AssemblyName[] referencedAssemblyNames = assembly.GetReferencedAssemblies();  
            foreach (var referencedAssemblyName in referencedAssemblyNames)
            {
                if (String.Equals(referencedAssemblyName.Name, WebJobsAssemblyName, StringComparison.OrdinalIgnoreCase))
                {
                    // the assembly references our core SDK assembly
                    // containing our built in attribute types
                    return true;
                }

                if (extensionAssemblies.Any(p => string.Equals(referencedAssemblyName.Name, p.GetName().Name, StringComparison.OrdinalIgnoreCase)))
                {
                    // the assembly references an extension assembly that may
                    // contain extension attributes
                    return true;
                }
            }

            return false;
        }

        public IReadOnlyList<Type> GetTypes()
        {
            List<Type> allTypes = new List<Type>();

            var assemblies = GetUserAssemblies();
            IEnumerable<Assembly> extensionAssemblies = _extensions.GetExtensionAssemblies();
            foreach (var assembly in assemblies)
            {
                var assemblyTypes = FindTypes(assembly, extensionAssemblies);
                if (assemblyTypes != null)
                {
                    allTypes.AddRange(assemblyTypes.Where(IsJobClass));
                }
            }

            return allTypes;
        }

        public static bool IsJobClass(Type type)
        {
            if (type == null)
            {
                return false;
            }

            return type.IsClass
                // For C# static keyword classes, IsAbstract and IsSealed both return true. Include C# static keyword
                // classes but not C# abstract keyword classes.
                && (!type.IsAbstract || type.IsSealed)
                // We only consider public top-level classes as job classes. IsPublic returns false for nested classes,
                // regardless of visibility modifiers. 
                && type.IsPublic
                && !type.ContainsGenericParameters;
        }

        private static IEnumerable<Assembly> GetUserAssemblies()
        {
            return AppDomain.CurrentDomain.GetAssemblies();
        }

        private Type[] FindTypes(Assembly assembly, IEnumerable<Assembly> extensionAssemblies)
        {
            // Only try to index assemblies that reference the core SDK assembly containing
            // binding attributes (or any registered extension assemblies). This ensures we
            // don't do more inspection work that is necessary during function indexing.
            if (!AssemblyReferencesSdkOrExtension(assembly, extensionAssemblies))
            {
                return null;
            }

            Type[] types = null;

            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                _log.WriteLine("Warning: Only got partial types from assembly: {0}", assembly.FullName);
                _log.WriteLine("Exception message: {0}", ex.ToString());

                // In case of a type load exception, at least get the types that did succeed in loading
                types = ex.Types;
            }
            catch (Exception ex)
            {
                _log.WriteLine("Warning: Failed to get types from assembly: {0}", assembly.FullName);
                _log.WriteLine("Exception message: {0}", ex.ToString());
            }

            return types;
        }
    }
}
