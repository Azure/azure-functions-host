#r "Twilio"

using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

public static void Run(string input, TraceWriter log)
{
    log.Info(input);
    var message = new CreateMessageOptions(new PhoneNumber("+1704XXXXXXX"));
}
