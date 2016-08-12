﻿module.exports = function (context, req) {
    try {
        //build the response string using the parameters extracted from the routes
        var parameters = req.params;
        var responseString = "";
        responseString += "String value: " + parameters.strvalue + "\n";
        responseString += "Bool value: " + parameters.boolvalue + "\n";
        responseString += "Long value: " + parameters.longvalue + "\n";
        responseString += "Double value: " + parameters.doubvalue + "\n";
        responseString += "Date value: " + parameters.date.toString() + "\n";
        context.res = {
            status: 200,
            body: responseString
        }
    } catch (e) {
        context.res = {
            status: 400,
            body: e.toString()
        };
    }

    context.res.headers = {
        'Content-Type': 'text/plain'
    };

    context.done();
}
