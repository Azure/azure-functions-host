import json
import azure.functions as func
import datetime
import logging
import os

app = func.FunctionApp(auth_level = func.AuthLevel.ANONYMOUS)

@app.function_name(name="HttpTrigger1")
@app.route(route="HttpTrigger1")
def test_function(req: func.HttpRequest) -> func.HttpResponse:
     return func.HttpResponse("HttpTrigger1 function processed a request!")


@app.function_name(name="HttpTrigger2")
@app.route(route="HttpTrigger2")
def test_function2(req: func.HttpRequest) -> func.HttpResponse:
     return func.HttpResponse("HttpTrigger2 function processed a request!")