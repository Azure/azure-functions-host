// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

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

            ConsoleLogger logger = new ConsoleLogger();
            string sourcePath = args[0];

            try
            {
                ExtensionsMetadataGenerator.Generate(sourcePath, args[1], logger);
            }
            catch (Exception ex)
            {
                logger.LogError("Error generating extension metadata: " + ex.ToString());
                throw;
            }
        }
    }
}
