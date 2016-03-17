module.exports = function (context, input) {
    context.log('Queue triggerd Node.js function input: ' + input);
    context.log('Sending Template Notification...');
    context.bindings.notification = {
        message: "Hello from Node! ",
        location:"Redmond"
    };
    context.done();
}