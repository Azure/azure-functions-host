module.exports = function (context, input) {
    context.log("TestResult:", {
        isBuffer: Buffer.isBuffer(input),
        length: input.length
    });

    context.bindings.output = input;
    context.done();
}