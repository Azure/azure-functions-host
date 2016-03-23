module.exports = function (context, input) {
    context.log('Node.js triggered function via EventHub called with input' , input);

    // queue to event hub 
    context.bindings.output = {
        prop1: "from test",
        id: input
    };

    context.done();
}