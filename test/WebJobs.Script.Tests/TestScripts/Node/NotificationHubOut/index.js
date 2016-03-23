module.exports = function (context, input) {
    context.log('Queue triggerd Node.js function input:', input);
    context.log('Sending Template Notification...');

    context.bindings.notification = {
        location: "Redmond",
        message: "Hello from Node!"
    };

    context.done();
}