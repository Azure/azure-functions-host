module.exports = function (context, input) {
    var bindingData = context.bindingData;
    var result = {
        isBuffer: Buffer.isBuffer(input),
        length: input.length,
        invocationId: bindingData.invocationId,
        blobMetadata: {
            path: bindingData.blobTrigger,
            properties: bindingData.properties,
            metadata: bindingData.metadata
        }
    };

    context.log("TestResult:", JSON.stringify(result));
    context.bindings.output = input;
    context.done();
}