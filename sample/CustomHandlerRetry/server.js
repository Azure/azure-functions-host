const express = require('express')
const app = express()
const port = process.env.functions_httpworker_port

app.use(
    express.urlencoded({
        extended: true
    })
)

app.use(express.json())

count = 0;
app.post('/HttpTrigger', (req, res) => {
    let retryCount = req.body.Metadata.RetryContext.RetryCount;
    let maxRetry = req.body.Metadata.RetryContext.MaxRetryCount;
    var response = {
        "Outputs": {
            "res": {
                "body": "Retry Count:" + retryCount + " Max Retry Count:" + maxRetry
            }
        },
        "Logs": null,
        "ReturnValue": null
    }
    if (retryCount < maxRetry) {
        res.status(500).send(response)
    }
    else {
        res.status(200).send(response)
    }
})

app.listen(port, () => {
    console.log(`Example app listening on port ${port}`)
})