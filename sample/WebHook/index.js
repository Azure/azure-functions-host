module.exports = function (context) {
    context.log('WebHook request triggered!');

    var res = {
        status: 200,
        body: 'WebHook processed successfully :)'
    };

    context.done(null, res);
}