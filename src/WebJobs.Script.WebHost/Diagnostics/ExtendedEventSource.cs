// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public abstract unsafe class ExtendedEventSource : EventSource
    {
        [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", Scope = "For this type", Target = "Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics")]
        [NonEvent]
        [Obsolete("Do not use params object[] overload, create a explicit WriteEvent in ExtendedEventSource.", true)]
        protected new void WriteEvent(int eventNum, params object[] args)
        {
            base.WriteEvent(eventNum, args);
        }

        // LogFunctionExecutionAggregateEvent
        [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly")]
        [NonEvent]
        protected void WriteEvent(int eventNumber, string a, string b, ulong c, ulong d, ulong e, ulong f)
        {
            const int count = 6;
            fixed (char* aPtr = a, bPtr = b)
            {
                EventData* data = stackalloc EventData[count];
                data[0].DataPointer = (IntPtr)aPtr;
                data[0].Size = (a.Length + 1) * sizeof(char);
                data[1].DataPointer = (IntPtr)bPtr;
                data[1].Size = (b.Length + 1) * sizeof(char);
                data[2].DataPointer = (IntPtr)(&c);
                data[2].Size = sizeof(ulong);
                data[3].DataPointer = (IntPtr)(&d);
                data[3].Size = sizeof(ulong);
                data[4].DataPointer = (IntPtr)(&e);
                data[4].Size = sizeof(ulong);
                data[5].DataPointer = (IntPtr)(&f);
                data[5].Size = sizeof(ulong);

                WriteEventCore(eventNumber, count, data);
            }
        }

        // LogFunctionDetailsEvent
        [NonEvent]
        [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly")]
        protected void WriteEvent(int eventNum, string a, string b, string c, string d, string e, bool f)
        {
            const int count = 6;
            fixed (char* aPtr = a,
                bPtr = b,
                cPtr = c,
                dPtr = d,
                ePtr = e)
            {
                EventData* data = stackalloc EventData[count];
                data[0].DataPointer = (IntPtr)aPtr;
                data[0].Size = (a.Length + 1) * sizeof(char);
                data[1].DataPointer = (IntPtr)bPtr;
                data[1].Size = (b.Length + 1) * sizeof(char);
                data[2].DataPointer = (IntPtr)cPtr;
                data[2].Size = (c.Length + 1) * sizeof(char);
                data[3].DataPointer = (IntPtr)dPtr;
                data[3].Size = (d.Length + 1) * sizeof(char);
                data[4].DataPointer = (IntPtr)ePtr;
                data[4].Size = (e.Length + 1) * sizeof(char);
                data[5].DataPointer = (IntPtr)(&f);
                data[5].Size = 4; // boolean variables have size 4

                WriteEventCore(eventNum, count, data);
            }
        }

        // LogFunctionExecutionEvent
        [NonEvent]
        [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly")]
        protected void WriteEvent(int eventNum, string a, string b, int c, string d, string e, string f, ulong g, bool h)
        {
            const int count = 8;
            fixed (char* aPtr = a,
                bPtr = b,
                dPtr = d,
                ePtr = e,
                fPtr = f)
            {
                EventData* data = stackalloc EventData[count];
                data[0].DataPointer = (IntPtr)aPtr;
                data[0].Size = (a.Length + 1) * sizeof(char);
                data[1].DataPointer = (IntPtr)bPtr;
                data[1].Size = (b.Length + 1) * sizeof(char);
                data[2].DataPointer = (IntPtr)(&c);
                data[2].Size = sizeof(int);
                data[3].DataPointer = (IntPtr)dPtr;
                data[3].Size = (d.Length + 1) * sizeof(char);
                data[4].DataPointer = (IntPtr)ePtr;
                data[4].Size = (e.Length + 1) * sizeof(char);
                data[5].DataPointer = (IntPtr)fPtr;
                data[5].Size = (f.Length + 1) * sizeof(char);
                data[6].DataPointer = (IntPtr)(&g);
                data[6].Size = sizeof(ulong);
                data[7].DataPointer = (IntPtr)(&h);
                data[7].Size = 4; // boolean variables have size 4

                WriteEventCore(eventNum, count, data);
            }
        }

        // RaiseFunctionsEventVerbose/Info/Warning
        [NonEvent]
        [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly")]
        protected void WriteEvent(int eventNum, string a, string b, string c, string d, string e, string f, string g, string h, string i, string j, string k, string l)
        {
            int count = 12;
            fixed (char* aPtr = a,
                bPtr = b,
                cPtr = c,
                dPtr = d,
                ePtr = e,
                fPtr = f,
                gPtr = g,
                hPtr = h,
                iPtr = i,
                jPtr = j,
                kPtr = k,
                lPtr = l)
            {
                EventData* data = stackalloc EventData[count];
                data[0].DataPointer = (IntPtr)aPtr;
                data[0].Size = (a.Length + 1) * sizeof(char);
                data[1].DataPointer = (IntPtr)bPtr;
                data[1].Size = (b.Length + 1) * sizeof(char);
                data[2].DataPointer = (IntPtr)cPtr;
                data[2].Size = (c.Length + 1) * sizeof(char);
                data[3].DataPointer = (IntPtr)dPtr;
                data[3].Size = (d.Length + 1) * sizeof(char);
                data[4].DataPointer = (IntPtr)ePtr;
                data[4].Size = (e.Length + 1) * sizeof(char);
                data[5].DataPointer = (IntPtr)fPtr;
                data[5].Size = (f.Length + 1) * sizeof(char);
                data[6].DataPointer = (IntPtr)gPtr;
                data[6].Size = (g.Length + 1) * sizeof(char);
                data[7].DataPointer = (IntPtr)hPtr;
                data[7].Size = (h.Length + 1) * sizeof(char);
                data[8].DataPointer = (IntPtr)iPtr;
                data[8].Size = (i.Length + 1) * sizeof(char);
                data[9].DataPointer = (IntPtr)jPtr;
                data[9].Size = (j.Length + 1) * sizeof(char);
                data[10].DataPointer = (IntPtr)kPtr;
                data[10].Size = (k.Length + 1) * sizeof(char);
                data[11].DataPointer = (IntPtr)lPtr;
                data[11].Size = (l.Length + 1) * sizeof(char);
                WriteEventCore(eventNum, count, data);
            }
        }

        // RaiseFunctionsEventError
        [NonEvent]
        [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly")]
        protected void WriteEvent(int eventNum, string a, string b, string c, string d, string e, string f, string g, string h, string i, string j, string k, string l, string m, string n)
        {
            int count = 14;
            fixed (char* aPtr = a,
                bPtr = b,
                cPtr = c,
                dPtr = d,
                ePtr = e,
                fPtr = f,
                gPtr = g,
                hPtr = h,
                iPtr = i,
                jPtr = j,
                kPtr = k,
                lPtr = l,
                mPtr = m,
                nPtr = n)
            {
                EventData* data = stackalloc EventData[count];
                data[0].DataPointer = (IntPtr)aPtr;
                data[0].Size = (a.Length + 1) * sizeof(char);
                data[1].DataPointer = (IntPtr)bPtr;
                data[1].Size = (b.Length + 1) * sizeof(char);
                data[2].DataPointer = (IntPtr)cPtr;
                data[2].Size = (c.Length + 1) * sizeof(char);
                data[3].DataPointer = (IntPtr)dPtr;
                data[3].Size = (d.Length + 1) * sizeof(char);
                data[4].DataPointer = (IntPtr)ePtr;
                data[4].Size = (e.Length + 1) * sizeof(char);
                data[5].DataPointer = (IntPtr)fPtr;
                data[5].Size = (f.Length + 1) * sizeof(char);
                data[6].DataPointer = (IntPtr)gPtr;
                data[6].Size = (g.Length + 1) * sizeof(char);
                data[7].DataPointer = (IntPtr)hPtr;
                data[7].Size = (h.Length + 1) * sizeof(char);
                data[8].DataPointer = (IntPtr)iPtr;
                data[8].Size = (i.Length + 1) * sizeof(char);
                data[9].DataPointer = (IntPtr)jPtr;
                data[9].Size = (j.Length + 1) * sizeof(char);
                data[10].DataPointer = (IntPtr)kPtr;
                data[10].Size = (k.Length + 1) * sizeof(char);
                data[11].DataPointer = (IntPtr)lPtr;
                data[11].Size = (l.Length + 1) * sizeof(char);
                data[12].DataPointer = (IntPtr)mPtr;
                data[12].Size = (m.Length + 1) * sizeof(char);
                data[13].DataPointer = (IntPtr)nPtr;
                data[13].Size = (n.Length + 1) * sizeof(char);
                WriteEventCore(eventNum, count, data);
            }
        }

        // LogFunctionMetricEvent
        [NonEvent]
        [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly")]
        protected void WriteEvent(int eventNum, string a, string b, string c, string d, long e, long f, long g, long h, string i, string j, string k, string l)
        {
            int count = 12;
            fixed (char* aPtr = a,
                bPtr = b,
                cPtr = c,
                dPtr = d,
                iPtr = i,
                jPtr = j,
                kPtr = k,
                lPtr = l)
            {
                EventData* data = stackalloc EventData[count];
                data[0].DataPointer = (IntPtr)aPtr;
                data[0].Size = (a.Length + 1) * sizeof(char);
                data[1].DataPointer = (IntPtr)bPtr;
                data[1].Size = (b.Length + 1) * sizeof(char);
                data[2].DataPointer = (IntPtr)cPtr;
                data[2].Size = (c.Length + 1) * sizeof(char);
                data[3].DataPointer = (IntPtr)dPtr;
                data[3].Size = (d.Length + 1) * sizeof(char);
                data[4].DataPointer = (IntPtr)(&e);
                data[4].Size = sizeof(long);
                data[5].DataPointer = (IntPtr)(&f);
                data[5].Size = sizeof(long);
                data[6].DataPointer = (IntPtr)(&g);
                data[6].Size = sizeof(long);
                data[7].DataPointer = (IntPtr)(&h);
                data[7].Size = sizeof(long);
                data[8].DataPointer = (IntPtr)iPtr;
                data[8].Size = (i.Length + 1) * sizeof(char);
                data[9].DataPointer = (IntPtr)jPtr;
                data[9].Size = (j.Length + 1) * sizeof(char);
                data[10].DataPointer = (IntPtr)kPtr;
                data[10].Size = (k.Length + 1) * sizeof(char);
                data[11].DataPointer = (IntPtr)lPtr;
                data[11].Size = (l.Length + 1) * sizeof(char);

                WriteEventCore(eventNum, count, data);
            }
        }

        // RaiseFunctionsDiagnostic
        [NonEvent]
        [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly")]
        protected void WriteEvent(int eventNum, string a, string b, string c, string d, string e, string f)
        {
            int count = 6;
            fixed (char* aPtr = a,
                bPtr = b,
                cPtr = c,
                dPtr = d,
                ePtr = e,
                fPtr = f)
            {
                EventData* data = stackalloc EventData[count];
                data[0].DataPointer = (IntPtr)aPtr;
                data[0].Size = (a.Length + 1) * sizeof(char);
                data[1].DataPointer = (IntPtr)bPtr;
                data[1].Size = (b.Length + 1) * sizeof(char);
                data[2].DataPointer = (IntPtr)cPtr;
                data[2].Size = (c.Length + 1) * sizeof(char);
                data[3].DataPointer = (IntPtr)dPtr;
                data[3].Size = (d.Length + 1) * sizeof(char);
                data[4].DataPointer = (IntPtr)ePtr;
                data[4].Size = (e.Length + 1) * sizeof(char);
                data[5].DataPointer = (IntPtr)fPtr;
                data[5].Size = (f.Length + 1) * sizeof(char);
                WriteEventCore(eventNum, count, data);
            }
        }
    }
}