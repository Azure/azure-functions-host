// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Colors.Net;
using Colors.Net.StringColorExtensions;

namespace WebJobs.Script.Cli
{
    internal static class Utilities
    {
        public static async Task SafeGuardAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception e)
            {
                System.Diagnostics.Trace.TraceError($"SafeGuard Exception: {e.ToString()}");
            }
        }

        public static async Task<T> SafeGuardAsync<T>(Func<Task<T>> action)
        {
            try
            {
                return await action();
            }
            catch (Exception e)
            {
                System.Diagnostics.Trace.TraceError($"SafeGuard<T> Exception: {e.ToString()}");
                return default(T);
            }
        }

        public static T SafeGuard<T>(Func<T> action)
        {
            try
            {
                return action();
            }
            catch
            {
                return default(T);
            }
        }

        internal static void PrintLogo()
        {
            ColoredConsole.WriteLine($@"
                  {AlternateLogoColor("%%%%%%")}
                 {AlternateLogoColor("%%%%%%")}
            @   {AlternateLogoColor("%%%%%%")}    @
          @@   {AlternateLogoColor("%%%%%%")}      @@
       @@@    {AlternateLogoColor("%%%%%%%%%%%", 3)}    @@@
     @@      {AlternateLogoColor("%%%%%%%%%%", 7)}        @@
       @@         {AlternateLogoColor("%%%%", 1)}       @@
         @@      {AlternateLogoColor("%%%")}       @@
           @@    {AlternateLogoColor("%%")}      @@
                {AlternateLogoColor("%%")}
                {AlternateLogoColor("%")}"
                .Replace("@", "@".DarkCyan().ToString()))
                .WriteLine();
        }

        private static RichString AlternateLogoColor(string str, int firstColorCount = -1)
        {
            if (str.Length == 1)
            {
                return str.DarkYellow();
            }
            else if (firstColorCount != -1)
            {
                return str.Substring(0, firstColorCount).Yellow() + str.Substring(firstColorCount).DarkYellow();
            }
            else
            {
                return str.Substring(0, str.Length / 2).Yellow() + str.Substring(str.Length / 2).DarkYellow();
            }
        }
    }
}
