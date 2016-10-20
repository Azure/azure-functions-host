module.exports = function (context, req) {
    context.log('Sending Bot message', req.body);

    var message = {
        source: 'Azure Functions (Node.js)!',
        message: req.body
    };

    context.done(null, message);
}