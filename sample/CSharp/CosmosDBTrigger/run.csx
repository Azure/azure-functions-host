#r "Microsoft.Azure.Documents.Client"
using Microsoft.Azure.Documents;
using System.Collections.Generic;
using System;
public static void Run(IReadOnlyList<Document> input, TraceWriter log)
{
    log.Verbose("Documents modified " + input.Count);
    log.Verbose("First document Id " + input[0].Id);
}
