module.exports = function (input, context) {
    context.log('Node.js triggered function via EventHub called with input ' + input);

    // queue to event hub 
    context.done(null, {
        prop1: "from test",
        id: input
    });
}