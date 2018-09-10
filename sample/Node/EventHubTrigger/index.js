var util = require('util');

module.exports = function (context, input) {
    context.log(util.format("Node.js script processed %d events", input.length));
    context.log("IsArray", util.isArray(input));

    for (i = 0; i < input.length; i++)
    {
        // EventData properties can be accessed via binding data,
        // including custom properties, system properties, etc.
        var bindingData = context.bindingData,
            eventProperties = bindingData.propertiesArray[i],
            systemProperties = bindingData.systemPropertiesArray[i],
            id = input[i].value;

        context.log('EventId: %s, EnqueuedTime: %s, Sequence: %d, Index: %s',
            id,
            bindingData.enqueuedTimeUtcArray[i],
            bindingData.sequenceNumberArray[i],
            eventProperties.testIndex);
    }

    context.done();
}