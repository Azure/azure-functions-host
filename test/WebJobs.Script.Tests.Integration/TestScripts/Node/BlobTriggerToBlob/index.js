module.exports = function (context, input) {
    var result = {
        isBuffer: Buffer.isBuffer(input),
        length: input.length,
        path: context.bindingData.blobTrigger,
        invocationId: context.bindingData.invocationId
    };

    context.log("TestResult:", JSON.stringify(result));
    context.bindings.output = input;
    context.done();
}