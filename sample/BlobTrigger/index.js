module.exports = function (context, blob) {
    console.log('Node.js blob trigger function processed blob ' + blob);
    context.done(null, {
        output: blob
    });
}