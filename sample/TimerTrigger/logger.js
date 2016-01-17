module.exports.log = function (text, log, callback) {
    var timeStamp = new Date().toISOString();
    log(timeStamp + ' ' + text);
    callback();
}