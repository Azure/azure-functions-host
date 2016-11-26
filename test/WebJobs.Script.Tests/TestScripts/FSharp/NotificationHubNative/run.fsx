open System

let Run(input: string, wnsToastPayload: byref<string>) =
    wnsToastPayload <- "<toast><visual><binding template=\"ToastText01\"><text id=\"1\">Test message from F#</text></binding></visual></toast>";
