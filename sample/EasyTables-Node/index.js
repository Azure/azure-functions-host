module.exports = function (context, input) {
    context.log('Node.js triggered function via EasyTables called with input ' + input);

    context.bindings.item = {
        Text: "Hello from Node! " + input
    };

    context.done();
}