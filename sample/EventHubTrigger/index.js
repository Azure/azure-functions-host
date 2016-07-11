module.exports = function (context, input) {
    context.log('Node.js script processed queue message', input.prop1);

    // prevent an infinite loop (since we're writing back
    // to the same hub we're triggering on)
    if (input.val1 < 3)
    {
        input.val1++;
        input.prop1 = "third";
        context.bindings.output = input;
    }

    context.done();
}