// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
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
            if (args.Length < 2)
            {
                System.Console.WriteLine("Usage: ");
                System.Console.WriteLine("metadatagen <sourcepath> <output>");

                return;
            }

            string sourcePath = args[0];
            AssemblyLoader.Initialize(sourcePath);
            ExtensionsMetadataGenerator.Generate(sourcePath, args[1], s => { });
        }

        private class AssemblyLoader
        {
            private static int _initialized;

            public static void Initialize(string basePath)
            {
                if (Interlocked.CompareExchange(ref _initialized, 1, 0) == 0)
                {
                    AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
                    {
                        string assemblyName = new AssemblyName(args.Name).Name;
                        string assemblyPath = Path.Combine(basePath, assemblyName + ".dll");

                        if (File.Exists(assemblyPath))
                        {
                            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);

                            return assembly;
                        }

                        return null;
                    };
                }
            }
        }
    }
}
