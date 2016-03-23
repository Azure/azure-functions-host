module.exports = function (context, data) {
    context.log('GitHub WebHook triggered! %s', data.comment.body);
    context.res = {
        body: 'New GitHub comment: ' + data.comment.body
    };
    context.done();
}