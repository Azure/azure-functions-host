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
        public static void Main(string[] args)
        {
            try
            {
                if (args.Length < 2)
                {
                    System.Console.WriteLine("Usage: ");
                    System.Console.WriteLine("metadatagen <sourcepath> <output>");

                    return;
                }

                string sourcePath = args[0];
                AssemblyLoader.Initialize(sourcePath, Log);
                ExtensionsMetadataGenerator.Generate(sourcePath, args[1], Log);
            }
            catch (Exception ex)
            {
                Log("Error generating extension metadata: " + ex.ToString());
                throw;
            }
        }

        private static void Log(string message)
        {
            System.Console.Error.Write(message);
        }

        private class AssemblyLoader
        {
            private static int _initialized;

            public static void Initialize(string basePath, Action<string> logger)
            {
                if (Interlocked.CompareExchange(ref _initialized, 1, 0) == 0)
                {
                    AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
                    {
                        try
                        {
                            string assemblyName = new AssemblyName(args.Name).Name;
                            string assemblyPath = Path.Combine(basePath, assemblyName + ".dll");

                            if (File.Exists(assemblyPath))
                            {
                                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
                                return assembly;
                            }

                            // If the assembly file is not found, it may be a runtime assembly for a different
                            // runtime version (i.e. the Function app assembly targets .NET Core 2.2, yet this
                            // process is running 2.0). In that case, just try to return the currently-loaded assembly,
                            // even if it's the wrong version; we won't be running it, just reflecting.
                            try
                            {
                                var assembly = Assembly.Load(assemblyName);
                                return assembly;
                            }
                            catch (Exception exc)
                            {
                                // Log and continue. This will likely fail as the assembly won't be found, but we have a clear
                                // message now that can help us diagnose.
                                logger($"Unable to find fallback for assmebly `{assemblyName}`. {exc.GetType().Name} {exc.Message}");
                            }

                            return null;
                        }
                        catch (Exception ex)
                        {
                            logger($"Error resolving assembly '{args.Name}': {ex}");
                            throw;
                        }
                    };
                }
            }
        }
    }
}
