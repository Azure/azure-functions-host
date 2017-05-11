module.exports = function (context, blob) {
    context.log('Node.js blob trigger function processed blob', blob);
    context.done(null, blob);
};