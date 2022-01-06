#r "Microsoft.Azure.Documents.Client"
using Microsoft.Azure.Documents;
using System.Collections.Generic;
using System;
public static void Run(IReadOnlyList<Document> input, ILogger log)
{
    log.LogInformation("Documents modified " + input.Count);
    log.LogInformation("First document Id " + input[0].Id);
}
