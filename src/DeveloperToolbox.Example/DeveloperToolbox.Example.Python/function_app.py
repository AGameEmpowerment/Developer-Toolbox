import json
import azure.functions as func
from toolbox_samples.config import load_settings, work_message
from toolbox_samples.handlers import dispatch, consume_work
from toolbox_samples.resources import ResourceSamples

app = func.FunctionApp(http_auth_level=func.AuthLevel.FUNCTION)
samples = ResourceSamples(load_settings())


@app.route(route="samples/{resource}", methods=["POST"], trigger_arg_name="request")
def python_resource_sample(request: func.HttpRequest) -> func.HttpResponse:
    try:
        value = request.get_json()
    except ValueError:
        return func.HttpResponse(json.dumps({"error": "Invalid JSON body."}), status_code=400, mimetype="application/json")
    status, body = dispatch(request.route_params["resource"], value, samples)
    return func.HttpResponse(json.dumps(body), status_code=status, mimetype="application/json")


@app.route(route="work/{target}", methods=["POST"], trigger_arg_name="request")
def python_enqueue_work(request: func.HttpRequest) -> func.HttpResponse:
    try:
        value = work_message(request.get_json())
    except ValueError:
        return func.HttpResponse(json.dumps({"error": "Invalid work message."}), status_code=400, mimetype="application/json")
    target = request.route_params["target"]
    if target not in ("servicebus", "storage-queue", "sqs"):
        return func.HttpResponse(json.dumps({"error": "Unknown work target."}), status_code=404, mimetype="application/json")
    return func.HttpResponse(json.dumps(samples.enqueue_work(value, target)), status_code=202, mimetype="application/json")


@app.service_bus_queue_trigger(arg_name="message", queue_name="queue.python-work", connection="ServiceBusConnection")
def python_service_bus_work(message: func.ServiceBusMessage):
    consume_work(message.get_body(), samples)


@app.queue_trigger(arg_name="message", queue_name="toolbox-python-work", connection="ToolboxAzureStorage")
def python_storage_work(message: func.QueueMessage):
    consume_work(message.get_body(), samples)
