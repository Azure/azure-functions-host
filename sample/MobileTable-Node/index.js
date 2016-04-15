module.exports = function (context, input) {
    context.log('Node.js queue-triggered MobileTable-Node function called with input', input);

    context.bindings.item = {
        Text: "Hello from Node! " + input
    };

    context.done();
}