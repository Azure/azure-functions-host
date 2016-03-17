module.exports = function (context, input) {
    context.log('Node.js queue-triggered DocumentDB function called with input ' + input);

    context.bindings.item = {
        text: "Hello from Node! " + input
    };

    context.done();
}