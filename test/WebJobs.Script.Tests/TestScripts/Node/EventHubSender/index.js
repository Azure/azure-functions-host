module.exports = function (context, input) {
    context.log('Node.js function triggered with input' , input);

    // queue to event hub 
    context.bindings.output = {
        prop1: "from test",
        id: input
    };

    context.done();
}