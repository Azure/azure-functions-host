module.exports = function (context, input) {    

    context.bindings.itemOut = context.bindings.itemIn;
    context.bindings.itemOut.text = "This was updated!";

    context.done();
}