module.exports = function (context, input) {
    context.log('Node.js queue-triggered DocumentDB function called with input', input);
    context.done(null, {
        text: "Hello from Node! " + input
    });
}