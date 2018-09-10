module.exports = function (context, message) {
    context.log('Node.js ServiceBus topic trigger function processed message', message);
    context.done(null, message.value);
}