"""Runtime-independent handlers shared by Functions, Lambda, and unit tests."""
import json
from .config import work_message
from .resources import RESOURCE_NAMES


def dispatch(resource, value, samples):
    if resource not in RESOURCE_NAMES:
        return 404, {"error": "Unknown toolbox resource."}
    if not isinstance(value, dict) or not isinstance(value.get("text"), str) or not value["text"].strip() or len(value["text"]) > 1024:
        return 400, {"error": "Supply text of 1–1024 characters."}
    return 200, samples.run(resource, value["text"])


def consume_work(value, samples, target="blob"):
    return samples.write_receipt(work_message(json.loads(value) if isinstance(value, (str, bytes)) else value), target)


def lambda_handler(event, samples):
    if isinstance(event, dict) and isinstance(event.get("Records"), list):
        failures = []
        for record in event["Records"]:
            if not isinstance(record, dict) or not isinstance(record.get("messageId"), str) or record.get("eventSource") != "aws:sqs":
                raise ValueError("Expected an SQS event with message IDs.")
            try:
                consume_work(record.get("body"), samples, "s3")
            except Exception:
                failures.append({"itemIdentifier": record["messageId"]})
        return {"batchItemFailures": failures}
    context = event.get("requestContext") if isinstance(event, dict) else None
    http = context.get("http") if isinstance(context, dict) else None
    if not isinstance(event, dict) or event.get("version") != "2.0" or not isinstance(event.get("body"), str) or not isinstance(event.get("rawPath"), str) or event.get("isBase64Encoded") is True or not isinstance(http, dict) or http.get("method") != "POST":
        return response(400, {"error": "Expected an API Gateway v2 POST with a JSON body."})
    try:
        value = json.loads(event["body"])
    except ValueError:
        return response(400, {"error": "Invalid JSON body."})
    if event.get("rawPath") == "/work":
        try:
            work = work_message(value)
        except ValueError:
            return response(400, {"error": "Invalid work message."})
        return response(200, samples.write_receipt(work, "s3"))
    path = event.get("rawPath", "")
    resource = path.removeprefix("/samples/") if path.startswith("/samples/") else ""
    status, body = dispatch(resource, value, samples)
    return response(status, body)


def response(status, body):
    return {"statusCode": status, "headers": {"content-type": "application/json"}, "body": json.dumps(body), "isBase64Encoded": False}
