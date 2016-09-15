#r "SendGridMail"
#load "..\Shared\Message.csx"

using System;
using SendGrid;
using Microsoft.Azure.WebJobs.Host;

public static SendGridMessage Run(Order order, TraceWriter log)
{
    log.Info($"C# Queue trigger function processed order: {order.OrderId}");

    var message = new SendGridMessage()
    {
        Subject = string.Format("Thanks for your order (#{0})!", order.OrderId),
        Text = string.Format("{0}, your order ({1}) is being processed!", order.CustomerName, order.OrderId)
    };
    message.AddTo(order.CustomerEmail);

    return message;
}