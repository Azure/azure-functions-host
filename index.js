module.exports = function (input, context) {
    context.log("Node.js script processed queue message '" + input.prop1 + "'");
    input.val1++;

    input.prop1 = "third";

    context.done(null, input);
}