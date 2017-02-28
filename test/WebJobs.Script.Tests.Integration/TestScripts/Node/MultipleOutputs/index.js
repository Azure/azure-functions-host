module.exports = function (context, input) {
    context.bindings.blob1 = "Test Blob 1";
    context.bindings.blob2 = "Test Blob 2";
    context.done(null, "Test Blob 3");
}