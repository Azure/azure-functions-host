var fs = require('fs');

module.exports = function (context, timerInfo) {
    var timeStamp = new Date().toISOString();    
    fs.appendFile('joblog.txt', timeStamp + '\r\n', function (err) {
        context.bindings.output = "From timer trigger: " + timeStamp;
        context.done(err);
    });
}