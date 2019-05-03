// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;

namespace ExtensionsMetadataGenerator.Console
{
    public class Program
    {
        private static readonly Assembly _thisAssembly = typeof(Program).Assembly;

        public static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                System.Console.WriteLine("Usage: ");
                System.Console.WriteLine("metadatagen <sourcepath> <output>");

                return;
            }

            ConsoleLogger logger = new ConsoleLogger();
            string sourcePath = args[0];

            try
            {
                AssemblyLoader.Initialize(sourcePath, logger);
                ExtensionsMetadataGenerator.Generate(sourcePath, args[1], logger);
            }
            catch (Exception ex)
            {
                logger.LogError("Error generating extension metadata: " + ex.ToString());
                throw;
            }
        }

        private class AssemblyLoader
        {
            private static int _initialized;

            public static void Initialize(string basePath, ConsoleLogger logger)
            {
                if (Interlocked.CompareExchange(ref _initialized, 1, 0) == 0)
                {
                    AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
                    {
                        logger.LogMessage($"Resolving assembly: '{args.Name}'");

                        try
                        {
                            string assemblyName = new AssemblyName(args.Name).Name;
                            string assemblyPath = Path.Combine(basePath, assemblyName + ".dll");

                            // This indicates a recursive lookup. Abort here to prevent stack overflow.
                            if (args.RequestingAssembly == _thisAssembly)
                            {
                                logger.LogMessage($"Cannot load '{assemblyName}'. Aborting assembly resolution.");
                                return null;
                            }

                            if (File.Exists(assemblyPath))
                            {
                                Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
                                logger.LogMessage($"Assembly '{assemblyName}' loaded from '{assemblyPath}'.");
                                return assembly;
                            }

                            try
                            {
                                // If the assembly file is not found, it may be a runtime assembly for a different
                                // runtime version (i.e. the Function app assembly targets .NET Core 2.2, yet this
                                // process is running 2.0). In that case, just try to return the currently-loaded assembly,
                                // even if it's the wrong version; we won't be running it, just reflecting.
                                Assembly assembly = Assembly.Load(assemblyName);
                                logger.LogMessage($"Assembly '{assemblyName}' loaded.");
                                return assembly;
                            }
                            catch (Exception ex)
                            {
                                // We'll already log an error if this happens; this gives a little more details if debug is enabled.
                                logger.LogMessage($"Unable to find fallback for assembly '{assemblyName}'. {ex}");
                            }

                            return null;
                        }
                        catch (Exception ex)
                        {
                            logger.LogError($"Error resolving assembly '{args.Name}': {ex}");
                            throw;
                        }
                    };
                }
            }
        }
    }
}
