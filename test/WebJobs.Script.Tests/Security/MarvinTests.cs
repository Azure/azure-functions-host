// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Text;

using Xunit;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class MarvinTests
    {
        // In the spirit of cross-checking, these tests are pulled from a non-Microsoft
        // Marvin32 implementation. This implementation, per Marvin32 design team, is not
        // considered completely compliant/correct and so it should not be used. But the
        // simple test cases here do result in matching output.
        // https://github.com/skeeto/marvin32/blob/21020faea884799879492204af70414facfd27e9/marvin32.c#L112

        private const ulong Seed0 = 0x004fb61a001bdbcc;
        private const ulong Seed1 = 0x804fb61a001bdbcc;
        private const ulong Seed2 = 0x804fb61a801bdbcc;

        private TestCase[] s_testCases = new[]
        {
            new TestCase { Seed = Seed0, Text = string.Empty,  Checksum = 0x30ed35c100cd3c7d },
            new TestCase { Seed = Seed0, Text = "\xaf",  Checksum = 0x48e73fc77d75ddc1 },
            new TestCase { Seed = Seed0, Text = "\xe7\x0f",  Checksum = 0xb5f6e1fc485dbff8 },
            new TestCase { Seed = Seed0, Text = "\x37\xf4\x95",  Checksum = 0xf0b07c789b8cf7e8 },
            new TestCase { Seed = Seed0, Text = "\x86\x42\xdc\x59",  Checksum = 0x7008f2e87e9cf556 },
            new TestCase { Seed = Seed0, Text = "\x15\x3f\xb7\x98\x26",  Checksum = 0xe6c08c6da2afa997 },
            new TestCase { Seed = Seed0, Text = "\x09\x32\xe6\x24\x6c\x47",  Checksum = 0x6f04bf1a5ea24060 },
            new TestCase { Seed = Seed0, Text = "\xab\x42\x7e\xa8\xd1\x0f\xc7",  Checksum = 0xe11847e4f0678c41 },

            new TestCase { Seed = Seed1, Text = string.Empty,  Checksum = 0x10a9d5d3996fd65d },
            new TestCase { Seed = Seed1, Text = "\xaf",  Checksum = 0x68201f91960ebf91 },
            new TestCase { Seed = Seed1, Text = "\xe7\x0f",  Checksum = 0x64b581631f6ab378 },
            new TestCase { Seed = Seed1, Text = "\x37\xf4\x95",  Checksum = 0xe1f2dfa6e5131408 },
            new TestCase { Seed = Seed1, Text = "\x86\x42\xdc\x59",  Checksum = 0x36289d9654fb49f6 },
            new TestCase { Seed = Seed1, Text = "\x15\x3f\xb7\x98\x26",  Checksum = 0x0a06114b13464dbd },
            new TestCase { Seed = Seed1, Text = "\x09\x32\xe6\x24\x6c\x47",  Checksum = 0xd6dd5e40ad1bc2ed },
            new TestCase { Seed = Seed1, Text = "\xab\x42\x7e\xa8\xd1\x0f\xc7",  Checksum = 0xe203987dba252fb3 },

            new TestCase { Seed = Seed2, Text = "\x00",  Checksum = 0xa37fb0da2ecae06c },
            new TestCase { Seed = Seed2, Text = "\xff",  Checksum = 0xfecef370701ae054 },
            new TestCase { Seed = Seed2, Text = "\x00\xff",  Checksum = 0xa638e75700048880 },
            new TestCase { Seed = Seed2, Text = "\xff\x00",  Checksum = 0xbdfb46d969730e2a },
            new TestCase { Seed = Seed2, Text = "\xff\x00\xff",  Checksum = 0x9d8577c0fe0d30bf },
            new TestCase { Seed = Seed2, Text = "\x00\xff\x00",  Checksum = 0x4f9fbdde15099497 },
            new TestCase { Seed = Seed2, Text = "\x00\xff\x00\xff",  Checksum = 0x24eaa279d9a529ca },
            new TestCase { Seed = Seed2, Text = "\xff\x00\xff\x00",  Checksum = 0xd3bec7726b057943 },
            new TestCase { Seed = Seed2, Text = "\xff\x00\xff\x00\xff",  Checksum = 0x920b62bbca3e0b72 },
            new TestCase { Seed = Seed2, Text = "\x00\xff\x00\xff\x00",  Checksum = 0x1d7ddf9dfdf3c1bf },
            new TestCase { Seed = Seed2, Text = "\x00\xff\x00\xff\x00\xff",  Checksum = 0xec21276a17e821a5 },
            new TestCase { Seed = Seed2, Text = "\xff\x00\xff\x00\xff\x00",  Checksum = 0x6911a53ca8c12254 },
            new TestCase { Seed = Seed2, Text = "\xff\x00\xff\x00\xff\x00\xff",  Checksum = 0xfdfd187b1d3ce784 },
            new TestCase { Seed = Seed2, Text = "\x00\xff\x00\xff\x00\xff\x00",  Checksum = 0x71876f2efb1b0ee8 },
        };

        public static ulong GetDotNetCurrentMarvinDefaultSeed()
        {
            Type marvinType = typeof(object).Assembly.GetType("System.Marvin");

            FieldInfo fi = null;
            foreach (FieldInfo marvinField in marvinType.GetFields(BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (marvinField.Name.Contains("DefaultSeed"))
                {
                    fi = marvinField;
                    break;
                }
            }

            Assert.NotNull(fi);
            return (ulong)fi.GetValue(null);
        }

        private static void ValidateDotNetStringHashMatchesMarvin(string text)
        {
            ulong defaultSeed = GetDotNetCurrentMarvinDefaultSeed();

            int expected = text.GetHashCode();

            byte[] input = Encoding.Unicode.GetBytes(text);
            int marvin = Marvin.ComputeHash32(input.AsSpan(), defaultSeed);

            Assert.Equal(expected, marvin);
        }

        [Fact]
        public void Marvin_MatchesDotNetBehavior()
        {
            string text = "abcdefghijklmnopqrstuvwxyz";
            ValidateDotNetStringHashMatchesMarvin(text);
        }

        [Fact]
        public void Marvin_Basic()
        {
            // This test verifies that our C# implementation provides
            // the same result as SymCrypt for their standard test.
            // https://github.com/microsoft/SymCrypt/blob/master/lib/marvin32.c#L316
            ulong seed = 0xd53cd9cecd0893b7;

            string text = "abc";
            byte[] input = Encoding.ASCII.GetBytes(text);

            long expected = 0x22c74339492769bf;
            long marvin = Marvin.ComputeHash(input.AsSpan(), seed);
            Assert.Equal(expected, marvin);
        }

        [Fact]
        public void Marvin_VariousCases()
        {
            Encoding latin1 = Encoding.GetEncoding("ISO-8859-1");

            foreach (TestCase testCase in s_testCases)
            {
                string text = testCase.Text;
                byte[] input = latin1.GetBytes(text);
                ulong seed = testCase.Seed;

                Span<byte> bytes = input.AsSpan();

                long expected64 = (long)testCase.Checksum;

                long marvin64 = Marvin.ComputeHash(bytes, seed);
                Assert.Equal(expected64, marvin64);

                int expected32 = (int)(marvin64 ^ marvin64 >> 32);
                int marvin32 = Marvin.ComputeHash32(bytes, seed);
                Assert.Equal(expected32, marvin32);

                // Validate that our algorithm behavior matches the
                // built-in .NET Marvin32 algorithm encoded in
                // string.GetHashCode(). This code path processes
                // a UTF16 representation of inputs.
                ValidateDotNetStringHashMatchesMarvin(text);
            }
        }

        private class TestCase
        {
            public ulong Seed { get; set; }

            public string Text { get; set; }

            public ulong Checksum { get; set;  }
        }
    }
}
