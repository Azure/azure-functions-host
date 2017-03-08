module.exports = function (context, input) {    

    context.log(context.bindings);

    var relatedCount = context.bindings.relatedItems.length;
    if (relatedCount !== 3) {
        throw Error("Expected 3 documents. Found " + relatedCount);
    }

    context.bindings.itemOut = context.bindings.itemIn;
    context.bindings.itemOut.text = "This was updated!";

    context.done();
}