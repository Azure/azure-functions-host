module.exports = function (context, input) {

    context.log(context.bindings);

    if (context.bindings.items.length !== 2) {
       throw Error("Expected 2 documents. Found " + context.bindings.items.length);
    }

    context.bindings.blob = input.id;

    context.done();
}