module.exports = function (context, blob) {
    context.log('Node.js blob trigger function processed blob %s' , blob);
    context.done(null, {
        output: blob
    });
}