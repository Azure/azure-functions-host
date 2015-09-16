function scheduledJob(timerInfo, callback) {
    console.log('Node.js scheduled job function ran at ' + new Date().toISOString());
    callback();
}