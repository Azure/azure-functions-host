#r "SendGrid"
#load "..\Shared\Order.csx"

using SendGrid.Helpers.Mail;

public static Mail Run(Order order, TraceWriter log)
{
    log.Info($"C# Queue trigger function processed order: {order.OrderId}");

    var message = new Mail
    {
        Subject = $"Thanks for your order (#{order.OrderId})!"        
    };

    Content content = new Content
    {
        Type = "text/plain",
        Value = $"{order.CustomerName}, your order ({order.OrderId}) is being processed!"
    };
    message.AddContent(content);
  
    return message;
}