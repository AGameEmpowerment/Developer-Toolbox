"""SDK write/read/cleanup examples against running local toolbox containers only."""
import base64
import json
import smtplib
import time
import uuid
from contextlib import contextmanager
from email.message import EmailMessage

import boto3
import pika
import redis
import requests
from azure.cosmos import CosmosClient, PartitionKey
from azure.data.tables import TableServiceClient
from azure.servicebus import ServiceBusClient, ServiceBusMessage
from azure.storage.blob import BlobServiceClient
from azure.storage.queue import QueueServiceClient
from botocore.config import Config
from botocore.exceptions import ClientError

from .config import local_url, service_bus_connection, storage_connection, work_message

RESOURCE_NAMES = ("s3", "sqs", "sns", "eventbridge", "dynamodb", "rabbitmq", "servicebus", "blob", "storage-queue", "table", "redis", "cosmos", "seq", "wiremock", "mail")
AZURE_OPTIONS = {"retry_total": 0, "connection_timeout": 5, "read_timeout": 10}


@contextmanager
def cleanup_after(cleanup):
    failure = None
    try:
        yield
    except Exception as error:
        failure = error
        raise
    finally:
        try:
            cleanup()
        except Exception as error:
            if failure:
                raise ExceptionGroup("The sample and its cleanup failed.", [failure, error]) from None
            raise


def verify(actual, expected):
    if actual != expected:
        raise RuntimeError("The resource did not return the value written by this sample.")


class ResourceSamples:
    def __init__(self, settings, factories=None):
        self.settings = settings
        self.factories = factories or {}
        self.clients = {}

    def aws(self, service):
        endpoint = self.settings.get("Resources__DynamoDb__Endpoint", "http://localhost:10180") if service == "dynamodb" else self.settings.get("Resources__S3__Endpoint") or self.settings.get("TOOLBOX_AWS_ENDPOINT", "http://localhost:10195")
        local_url(endpoint, ("dynamodb",) if service == "dynamodb" else ("aws-emulator",))
        access = self.settings.get("Resources__AWS__AccessKey")
        secret = self.settings.get("Resources__AWS__SecretKey")
        if not access or not secret:
            raise ValueError("Set the local Resources__AWS__AccessKey and Resources__AWS__SecretKey settings.")
        if service not in self.clients:
            self.clients[service] = self.factories[service]() if service in self.factories else boto3.client(service, endpoint_url=endpoint, region_name="us-east-1", aws_access_key_id=access, aws_secret_access_key=secret, config=Config(connect_timeout=5, read_timeout=10, retries={"total_max_attempts": 1}, proxies={}, s3={"addressing_style": "path"}))
        return self.clients[service]

    def storage(self, kind):
        connection = storage_connection(self.settings)
        if kind not in self.clients:
            sdk = {"blob": BlobServiceClient, "queue": QueueServiceClient, "table": TableServiceClient}[kind]
            options = AZURE_OPTIONS | ({"api_version": "2026-06-06"} if kind in ("blob", "queue") else {})
            self.clients[kind] = self.factories[kind]() if kind in self.factories else sdk.from_connection_string(connection, **options)
        return self.clients[kind]

    def bus(self):
        connection = service_bus_connection(self.settings)
        if "servicebus" not in self.clients:
            self.clients["servicebus"] = self.factories["servicebus"]() if "servicebus" in self.factories else ServiceBusClient.from_connection_string(connection, retry_total=0, socket_timeout=5)
        return self.clients["servicebus"]

    def write_receipt(self, value, target="blob"):
        data = work_message(value)
        body = json.dumps(data)
        key = f"receipts/python/{data['id']}.json"
        if target == "s3":
            self.aws("s3").put_object(Bucket=self.settings.get("Resources__S3__Bucket", "toolbox-samples"), Key=key, Body=body.encode(), ContentType="application/json")
        else:
            container = self.storage("blob").get_container_client("toolbox-samples")
            try:
                container.create_container()
            except Exception as error:
                if getattr(error, "status_code", None) != 409:
                    raise
            container.upload_blob(key, body, overwrite=True)
        return data | {"receipt": key}

    def enqueue_work(self, value, target):
        data = work_message(value)
        if target == "servicebus":
            with self.bus().get_queue_sender("queue.python-work") as sender:
                sender.send_messages(ServiceBusMessage(json.dumps(data), message_id=data["id"]), timeout=10)
        elif target == "storage-queue":
            queue = self.storage("queue").get_queue_client("toolbox-python-work")
            try:
                queue.create_queue()
            except Exception as error:
                if getattr(error, "status_code", None) != 409:
                    raise
            queue.send_message(base64.b64encode(json.dumps(data).encode()).decode())
        elif target == "sqs":
            client = self.aws("sqs")
            queue = client.get_queue_url(QueueName="toolbox-python-lambda-work")["QueueUrl"]
            local_url(queue, ("aws-emulator",), allow_path=True)
            client.send_message(QueueUrl=queue, MessageBody=json.dumps(data))
        else:
            raise ValueError("Unknown work target.")
        return data

    def receive(self, queue, expected):
        client = self.aws("sqs")
        deadline = time.monotonic() + 15
        while time.monotonic() < deadline:
            messages = client.receive_message(QueueUrl=queue, WaitTimeSeconds=1, MaxNumberOfMessages=1).get("Messages", [])
            if messages:
                message = messages[0]
                value = json.loads(message["Body"])
                if "Message" in value:
                    value = json.loads(value["Message"])
                if "detail" in value:
                    value = value["detail"]
                verify(work_message(value), expected)
                client.delete_message(QueueUrl=queue, ReceiptHandle=message["ReceiptHandle"])
                return
        raise TimeoutError("The sample message was not delivered before the deadline.")

    def messaging(self, resource, data):
        sqs = self.aws("sqs")
        queue = sqs.create_queue(QueueName=f"toolbox-python-validation-{uuid.uuid4()}")["QueueUrl"]
        local_url(queue, ("aws-emulator",), allow_path=True)
        topic = None
        rule = None

        def cleanup():
            failures = []
            actions = []
            if topic:
                actions.append(lambda: self.aws("sns").delete_topic(TopicArn=topic))
            if rule:
                actions.extend((lambda: self.aws("events").remove_targets(Rule=rule, Ids=["queue"]), lambda: self.aws("events").delete_rule(Name=rule)))
            actions.append(lambda: sqs.delete_queue(QueueUrl=queue))
            for action in actions:
                try:
                    action()
                except Exception as error:
                    failures.append(error)
            if failures:
                raise ExceptionGroup("Temporary messaging resources could not be removed.", failures)

        with cleanup_after(cleanup):
            if resource == "sqs":
                sqs.send_message(QueueUrl=queue, MessageBody=json.dumps(data))
            else:
                arn = sqs.get_queue_attributes(QueueUrl=queue, AttributeNames=["QueueArn"])["Attributes"]["QueueArn"]
                policy = {"Version": "2012-10-17", "Statement": [{"Effect": "Allow", "Principal": {"Service": "sns.amazonaws.com" if resource == "sns" else "events.amazonaws.com"}, "Action": "sqs:SendMessage", "Resource": arn}]}
                sqs.set_queue_attributes(QueueUrl=queue, Attributes={"Policy": json.dumps(policy)})
                if resource == "sns":
                    client = self.aws("sns")
                    topic = client.create_topic(Name=f"toolbox-python-{data['id']}")["TopicArn"]
                    client.subscribe(TopicArn=topic, Protocol="sqs", Endpoint=arn)
                    client.publish(TopicArn=topic, Message=json.dumps(data))
                else:
                    client = self.aws("events")
                    rule = f"toolbox-python-{data['id']}"
                    client.put_rule(Name=rule, EventPattern=json.dumps({"source": ["toolbox.python"], "detail": {"id": [data["id"]]}}))
                    if client.put_targets(Rule=rule, Targets=[{"Id": "queue", "Arn": arn}])["FailedEntryCount"]:
                        raise RuntimeError("EventBridge rejected the SQS target.")
                    if client.put_events(Entries=[{"Source": "toolbox.python", "DetailType": "ToolboxSample", "Detail": json.dumps(data)}])["FailedEntryCount"]:
                        raise RuntimeError("EventBridge rejected the event.")
            self.receive(queue, data)
        return data

    def run(self, resource, text="Hello from Python"):
        if resource not in RESOURCE_NAMES:
            raise KeyError("Unknown toolbox resource.")
        data = work_message({"id": str(uuid.uuid4()), "language": "python", "text": text})
        body = json.dumps(data)
        name = f"toolbox-python-{data['id']}"
        if resource in ("sqs", "sns", "eventbridge"):
            return self.messaging(resource, data)
        if resource == "s3":
            client = self.aws("s3")
            key = {"Bucket": self.settings.get("Resources__S3__Bucket", "toolbox-samples"), "Key": f"samples/python/{data['id']}.json"}
            with cleanup_after(lambda: client.delete_object(**key)):
                client.put_object(**key, Body=body.encode())
                response = client.get_object(**key)["Body"]
                try:
                    verify(response.read().decode(), body)
                finally:
                    response.close()
            return data
        if resource == "blob":
            container = self.storage("blob").get_container_client(name)
            container.create_container()
            with cleanup_after(container.delete_container):
                container.upload_blob("sample.json", body)
                verify(container.download_blob("sample.json").readall().decode(), body)
            return data
        if resource == "storage-queue":
            client = self.storage("queue").get_queue_client(name)
            client.create_queue()
            with cleanup_after(client.delete_queue):
                client.send_message(body)
                messages = list(client.receive_messages(messages_per_page=1))
                verify(messages[0].content if messages else None, body)
            return data
        if resource == "table":
            service = self.storage("table")
            table = service.create_table(f"Python{data['id'].replace('-', '')}")
            with cleanup_after(lambda: service.delete_table(table.table_name)):
                table.create_entity({"PartitionKey": "samples", "RowKey": data["id"], "text": text})
                verify(table.get_entity("samples", data["id"])["text"], text)
            return data
        if resource == "servicebus":
            with self.bus().get_queue_sender("queue.python-validation") as sender, self.bus().get_queue_receiver("queue.python-validation", max_wait_time=1) as receiver:
                unmatched = []
                with cleanup_after(lambda: [receiver.abandon_message(message) for message in unmatched]):
                    sender.send_messages(ServiceBusMessage(body, message_id=data["id"]), timeout=10)
                    deadline = time.monotonic() + 15
                    while time.monotonic() < deadline:
                        for message in receiver.receive_messages(max_message_count=1, max_wait_time=1):
                            if str(message.message_id) == data["id"]:
                                verify(json.loads(str(message))["text"], text)
                                receiver.complete_message(message)
                                return data
                            unmatched.append(message)
                    raise TimeoutError("The Service Bus sample message was not delivered.")
        if resource == "rabbitmq":
            host = self.settings.get("RABBITMQ_HOST", "localhost")
            port = int(self.settings.get("RABBITMQ_PORT", 10190))
            local_url(f"amqp://{host}:{port}", ("rabbitmq",), schemes=("amqp",))
            user, password = self.settings.get("RABBITMQ_DEFAULT_USER"), self.settings.get("RABBITMQ_DEFAULT_PASS")
            if not user or not password:
                raise ValueError("Set local RabbitMQ credentials.")
            connection = self.factories["rabbitmq"]() if "rabbitmq" in self.factories else pika.BlockingConnection(pika.ConnectionParameters(host=host, port=port, credentials=pika.PlainCredentials(user, password), socket_timeout=5, stack_timeout=10, blocked_connection_timeout=10, heartbeat=5))
            with cleanup_after(connection.close):
                channel = connection.channel()
                channel.confirm_delivery()
                queue = channel.queue_declare(queue="", exclusive=True, auto_delete=True).method.queue
                channel.basic_publish(exchange="", routing_key=queue, body=body.encode(), mandatory=True)
                method, _, result = channel.basic_get(queue=queue, auto_ack=False)
                verify(result.decode() if result else None, body)
                channel.basic_ack(method.delivery_tag)
            return data
        if resource == "dynamodb":
            client = self.aws("dynamodb")
            table = "ToolboxSamplesPython"
            try:
                client.describe_table(TableName=table)
            except ClientError as error:
                if error.response["Error"]["Code"] != "ResourceNotFoundException":
                    raise
                client.create_table(TableName=table, AttributeDefinitions=[{"AttributeName": "id", "AttributeType": "S"}], KeySchema=[{"AttributeName": "id", "KeyType": "HASH"}], BillingMode="PAY_PER_REQUEST")
            key = {"TableName": table, "Key": {"id": {"S": data["id"]}}}
            with cleanup_after(lambda: client.delete_item(**key)):
                client.put_item(TableName=table, Item={"id": {"S": data["id"]}, "text": {"S": text}})
                verify(client.get_item(**key, ConsistentRead=True).get("Item", {}).get("text", {}).get("S"), text)
            return data
        if resource == "redis":
            endpoint = self.settings.get("Resources__Redis__ConnectionString", "localhost:10120")
            endpoint = endpoint if "://" in endpoint else f"redis://{endpoint}"
            local_url(endpoint, ("redis",), schemes=("redis", "rediss"))
            client = self.factories["redis"]() if "redis" in self.factories else redis.Redis.from_url(endpoint, socket_connect_timeout=5, socket_timeout=10, decode_responses=True)
            def cleanup():
                try:
                    client.delete(name)
                finally:
                    client.close()
            with cleanup_after(cleanup):
                client.set(name, body, ex=60)
                verify(client.get(name), body)
            return data
        if resource == "cosmos":
            endpoint = self.settings.get("Resources__Cosmos__Endpoint", "https://localhost:10081")
            local_url(endpoint, ("cosmosdb",))
            if not self.settings.get("Resources__Cosmos__Key"):
                raise ValueError("Set the public local Cosmos emulator key.")
            if "cosmos" not in self.clients:
                self.clients["cosmos"] = self.factories["cosmos"]() if "cosmos" in self.factories else CosmosClient(endpoint, credential=self.settings["Resources__Cosmos__Key"], connection_verify=self.settings.get("Resources__Cosmos__AllowUntrustedLocalCertificate", "").lower() != "true", enable_endpoint_discovery=False, **AZURE_OPTIONS)
            database = self.clients["cosmos"].create_database_if_not_exists("ToolboxSamples")
            container = database.create_container_if_not_exists(id="PythonSamples", partition_key=PartitionKey(path="/id"))
            with cleanup_after(lambda: container.delete_item(data["id"], partition_key=data["id"])):
                container.create_item(data)
                verify(container.read_item(data["id"], partition_key=data["id"])["text"], text)
            return data
        if resource == "mail":
            host = self.settings.get("Resources__Mail__Host", "localhost")
            port = int(self.settings.get("Resources__Mail__Port", 10130))
            local_url(f"smtp://{host}:{port}", ("smtp4dev",), schemes=("smtp",))
            client = self.factories["mail"]() if "mail" in self.factories else smtplib.SMTP(host, port, timeout=10)
            with cleanup_after(client.quit):
                message = EmailMessage()
                message["From"], message["To"], message["Subject"] = "toolbox@localhost", "developer@localhost", name
                message.set_content(body)
                if client.send_message(message):
                    raise RuntimeError("SMTP rejected the sample recipient.")
            return data
        endpoint = self.settings.get("Resources__Seq__Endpoint", "http://localhost:10150") if resource == "seq" else self.settings.get("Resources__WireMock__Endpoint", "http://localhost:10080")
        local_url(endpoint, ("otlp-seq",) if resource == "seq" else ("wiremock",))
        client = self.factories["http"]() if "http" in self.factories else requests.Session()
        client.trust_env = False
        def checked(method, path, **options):
            response = client.request(method, endpoint.rstrip("/") + path, timeout=(5, 10), allow_redirects=False, **options)
            if not 200 <= response.status_code < 300:
                raise RuntimeError(f"The local {resource} endpoint returned HTTP {response.status_code}.")
            return response
        with cleanup_after(client.close):
            if resource == "seq":
                headers = {"Content-Type": "application/vnd.serilog.clef"}
                if self.settings.get("Resources__Seq__ApiKey"):
                    headers["X-Seq-ApiKey"] = self.settings["Resources__Seq__ApiKey"]
                checked("POST", "/api/events/raw?clef", headers=headers, data=json.dumps({"@t": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), "@mt": "Toolbox Python sample {SampleId}", "SampleId": data["id"], "Text": text}) + "\n")
            else:
                checked("POST", "/__admin/mappings", json={"id": data["id"], "request": {"method": "GET", "url": f"/toolbox-python/{data['id']}"}, "response": {"status": 200, "body": body}})
                with cleanup_after(lambda: checked("DELETE", f"/__admin/mappings/{data['id']}")):
                    verify(checked("GET", f"/toolbox-python/{data['id']}").text, body)
        return data

    def close(self):
        failures = []
        for client in self.clients.values():
            try:
                client.close()
            except Exception as error:
                failures.append(error)
        self.clients.clear()
        if failures:
            raise ExceptionGroup("SDK client cleanup failed.", failures)
