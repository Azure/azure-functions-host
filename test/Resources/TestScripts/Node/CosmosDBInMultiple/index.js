module.exports = function (context, input) {

    context.log(context.bindings);

    if (context.bindings.items.length !== 2) {
       throw Error("Expected 2 documents. Found " + context.bindings.items.length);
    }

    context.bindings.itemOut = {
        id: input.id,
        text: "Hello from Node with multiple input bindings!"
    };

    context.done();
}