module.exports = function (context, body) {
    context.log('GitHub WebHook triggered! ' + body.comment.body);
    context.res = {
        body: 'New GitHub comment: ' + body.comment.body
    };
    context.done();
}