module.exports = function (context, input) {
    if (input.length < 5) {
        var append = new Buffer([4, 5]);
        context.bindings.output = Buffer.concat([input, append]);
    }
    else {
        context.log("TestResult:", {
            isBuffer: Buffer.isBuffer(input),
            length: input.length
        });
    }

    context.done();
}