var util = require('util');

module.exports = function (context, input) {
    var parsedInput = JSON.parse(input);
    context.log(util.format("Node.js script processed %d events", parsedInput.length));
    context.log("IsArray", util.isArray(parsedInput));

    for (i = 0; i < parsedInput.length; i++)
    {
        // EventData properties can be accessed via binding data,
        // including custom properties, system properties, etc.
        var bindingData = context.bindingData,
            eventProperties = bindingData.propertiesArray[i],
            systemProperties = bindingData.systemPropertiesArray[i],
            id = parsedInput[i].value;

        context.log('EventId: %s, EnqueuedTime: %s, Sequence: %d, Index: %s',
            id,
            bindingData.enqueuedTimeUtcArray[i],
            bindingData.sequenceNumberArray[i],
            eventProperties.testIndex);
    }

    context.done();
}