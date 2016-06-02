module.exports = function (context, payload) {
    context.log('GitHub WebHook triggered!', payload.comment.body);
    context.res = {
        body: 'New GitHub comment: ' + payload.comment.body
    };
    context.done();
}