var util = require('util');

module.exports = function (context, order) {
    context.log('Node.js queue trigger function processed order', order.orderId);

    var message = {
        subject: util.format('Thanks for your order (#%d)!', order.orderId),
        content: [{
            type: 'text/plain',
            value: util.format("%s, your order (%d) is being processed!", order.customerName, order.orderId)
        }]
    };

    context.done(null, message);
}