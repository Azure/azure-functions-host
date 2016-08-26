#r "SendGridMail"
#load "..\Shared\Message.csx"

using System;
using SendGrid;
using Microsoft.Azure.WebJobs.Host;

public static void Run(Order order, out SendGridMessage message, TraceWriter log)
{
    log.Info($"C# Queue trigger function processed order: {order.OrderId}");

    message = new SendGridMessage()
    {
        Subject = string.Format("Thanks for your order (#{0})!", order.OrderId),
        Text = string.Format("{0}, your order ({1}) is being processed!", order.CustomerName, order.OrderId)
    };
    message.AddTo(order.CustomerEmail);
}