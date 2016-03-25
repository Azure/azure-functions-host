module.exports = function (context, apihub) {
    context.log('Node.js apihub trigger function processed apihub', apihub);
    context.done(null, {
        output: apihub
    });
}