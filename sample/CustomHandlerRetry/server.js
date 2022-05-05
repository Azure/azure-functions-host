const express = require('express')
const app = express()
const port = process.env.functions_httpworker_port

app.use(
    express.urlencoded({
        extended: true
    })
)

app.use(express.json())

var response = {
    "Outputs": {
        "res": {
            "body": "OK"
        }
    },
    "Logs": null,
    "ReturnValue": null
}

app.post('/HttpTrigger', (req, res) => {
    if (req.body.Metadata.RetryContext) {
        res.status(500).send(response)
    }
    else {
        res.status(200).send(response)
    }
})

app.post('/TimerTrigger', (req, res) => {
    var errorString = 'An error occurred';
    var maxRetries = 4;
    var retryContext = req.body.Metadata.RetryContext;

    if (retryContext.MaxRetryCount != maxRetries) {
        console.log('Unexpected error');
        throw 'Unexpected error';
    } else {
        console.log('JavaScript HTTP trigger function processed a request. retryCount: ' + retryContext.RetryCount);

        if (retryContext.RetryCount < maxRetries) {
            console.log(errorString);
            throw errorString;
        }
        console.log('Execution completed');
        res.status(200).send(response)
    }
})

app.listen(port, () => {
    console.log(`Example app listening on port ${port}`)
})