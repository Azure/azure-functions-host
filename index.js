module.exports = function (context, input) {
    context.log("Node.js script processed queue message '" + input.prop1 + "'");

    input.val1++;
    input.prop1 = "third";
    context.bindings.output = input;

    context.done();
}