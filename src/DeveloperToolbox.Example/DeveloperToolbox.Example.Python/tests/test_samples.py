import io
import json
import tempfile
import unittest
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path
from types import SimpleNamespace
from threading import Thread
from unittest.mock import Mock, patch

from toolbox_samples.config import load_settings, local_url, storage_connection, service_bus_connection, work_message
from toolbox_samples.resources import ResourceSamples
from toolbox_samples.handlers import dispatch, consume_work, lambda_handler

CREDENTIALS = {"Resources__AWS__AccessKey": "test", "Resources__AWS__SecretKey": "test"}
WORK = {"id": "b5117c10-14a7-4bf9-8c13-28e8702a6d77", "language": "python", "text": "hello"}


class ConfigurationTests(unittest.TestCase):
    def test_storage_rejects_settings_the_sdk_could_interpret_differently(self):
        for queue_fields in (
            "QueueEndpoint=https://remote.example/;QueueEndpoint=http://localhost:10001/test;",
            "QueueEndpoint=http://localhost:10001/test;queueendpoint=https://remote.example/;",
            " QueueEndpoint=https://remote.example/;QueueEndpoint=http://localhost:10001/test;",
            "QueueEndpoint=http://localhost:10001/test;QueueSecondaryEndpoint=https://remote.example/;",
            "QueueEndpoint=http://localhost:10001/test;malformed;",
        ):
            connection = f"DefaultEndpointsProtocol=http;AccountName=test;AccountKey=dGVzdA==;BlobEndpoint=http://localhost:10000/test;{queue_fields}TableEndpoint=http://localhost:10002/test;"
            with self.subTest(queue_fields=queue_fields), self.assertRaisesRegex(ValueError, "local"):
                storage_connection({"ToolboxAzureStorage": connection})

    def test_bus_rejects_ambiguous_settings(self):
        for connection in (
            "Endpoint=sb://remote;Endpoint=sb://localhost;UseDevelopmentEmulator=true;",
            "Endpoint=sb://localhost;endpoint=sb://remote;UseDevelopmentEmulator=true;",
            "Endpoint=sb://localhost;UseDevelopmentEmulator=true;UseDevelopmentEmulator=false;",
        ):
            with self.subTest(connection=connection), self.assertRaisesRegex(ValueError, "local"):
                service_bus_connection({"ServiceBusConnection": connection})

    def test_storage_sdks_use_supported_azurite_api_with_signed_requests(self):
        requests = []

        class Handler(BaseHTTPRequestHandler):
            def do_PUT(self):
                requests.append(dict(self.headers))
                self.send_response(201)
                self.send_header("x-ms-request-id", "fake-request")
                self.send_header("x-ms-version", "2026-06-06")
                self.end_headers()

            def log_message(self, *_):
                pass

        server = HTTPServer(("127.0.0.1", 0), Handler)
        thread = Thread(target=lambda: server.serve_forever(poll_interval=0.05))
        thread.start()
        endpoint = f"http://127.0.0.1:{server.server_port}/test"
        connection = f"DefaultEndpointsProtocol=http;AccountName=test;AccountKey=dGVzdA==;BlobEndpoint={endpoint};QueueEndpoint={endpoint};TableEndpoint={endpoint};"
        samples = ResourceSamples({"Resources__AzureStorage__ConnectionString": connection})
        try:
            samples.storage("blob").create_container("sample")
            samples.storage("queue").create_queue("sample")
            self.assertEqual(2, len(requests))
            for request in requests:
                self.assertEqual("2026-06-06", request["x-ms-version"])
                self.assertTrue(request["Authorization"].startswith("SharedKey test:"))
        finally:
            samples.close()
            server.shutdown()
            server.server_close()
            thread.join(timeout=5)
            self.assertFalse(thread.is_alive())

    def test_unsafe_endpoints_are_rejected(self):
        for endpoint in ("https://s3.amazonaws.com", "http://localhost.evil.test", "http://user:secret@localhost", "http://localhost?redirect=cloud", "http://localhost/path", "http://localhost/#fragment", "file:///tmp/test", "ftp://localhost"):
            with self.subTest(endpoint=endpoint), self.assertRaises(ValueError):
                local_url(endpoint)

    def test_loopback_and_explicit_container_endpoints_are_allowed(self):
        for endpoint in ("http://localhost:10195", "http://127.0.0.2:10195", "http://[::1]:10195", "http://aws-emulator:5000"):
            with self.subTest(endpoint=endpoint):
                self.assertTrue(local_url(endpoint, ("aws-emulator",)))

    def test_settings_use_literal_environment_precedence(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "settings.json"
            path.write_text(json.dumps({"Resources__AWS__AccessKey": "file"}))
            self.assertEqual("$literal", load_settings(path, {"Resources__AWS__AccessKey": "$literal"})["Resources__AWS__AccessKey"])

    def test_runtime_without_repository_uses_environment_only(self):
        with patch("toolbox_samples.config.__file__", "/var/task/toolbox_samples/config.py"), patch.object(Path, "is_file", return_value=False), patch.object(Path, "exists", return_value=False):
            self.assertEqual({"Resources__AWS__AccessKey": "test"}, load_settings(environment={"Resources__AWS__AccessKey": "test"}))

    def test_storage_and_bus_require_explicit_emulator_configuration(self):
        with self.assertRaisesRegex(ValueError, "configure_examples"):
            storage_connection({"ToolboxAzureStorage": "UseDevelopmentStorage=true"})
        with self.assertRaisesRegex(ValueError, "UseDevelopmentEmulator"):
            service_bus_connection({"ServiceBusConnection": "Endpoint=sb://localhost;UseDevelopmentEmulator=false;"})

    def test_all_sdk_examples_reject_cloud_before_client_creation(self):
        cases = [(name, CREDENTIALS | {"TOOLBOX_AWS_ENDPOINT": "https://sqs.us-east-1.amazonaws.com"}) for name in ("s3", "sqs", "sns", "eventbridge")]
        cases += [("dynamodb", CREDENTIALS | {"Resources__DynamoDb__Endpoint": "https://dynamodb.us-east-1.amazonaws.com"})]
        cases += [(name, {"Resources__AzureStorage__ConnectionString": "DefaultEndpointsProtocol=https;AccountName=cloud;AccountKey=secret;BlobEndpoint=https://cloud.blob.core.windows.net;QueueEndpoint=https://cloud.queue.core.windows.net;TableEndpoint=https://cloud.table.core.windows.net;"}) for name in ("blob", "storage-queue", "table")]
        cases += [("servicebus", {"ServiceBusConnection": "Endpoint=sb://cloud.servicebus.windows.net;UseDevelopmentEmulator=true;"}), ("rabbitmq", {"RABBITMQ_HOST": "cloud.example"}), ("redis", {"Resources__Redis__ConnectionString": "cloud.example:6379"}), ("cosmos", {"Resources__Cosmos__Endpoint": "https://cloud.documents.azure.com"}), ("seq", {"Resources__Seq__Endpoint": "https://cloud.example"}), ("wiremock", {"Resources__WireMock__Endpoint": "https://cloud.example"}), ("mail", {"Resources__Mail__Host": "cloud.example"})]
        for name, settings in cases:
            with self.subTest(resource=name):
                factory = Mock(side_effect=AssertionError("Client should not be created"))
                samples = ResourceSamples(settings, dict.fromkeys(("s3", "sqs", "sns", "events", "dynamodb", "blob", "queue", "table", "servicebus", "rabbitmq", "redis", "cosmos", "http", "mail"), factory))
                with self.assertRaisesRegex(ValueError, "local"):
                    samples.run(name)
                factory.assert_not_called()

    def test_aws_never_uses_ambient_credentials(self):
        factory = Mock()
        with self.assertRaisesRegex(ValueError, "AccessKey"):
            ResourceSamples({}, {"s3": factory}).run("s3")
        factory.assert_not_called()


class ResourceTests(unittest.TestCase):
    def test_close_attempts_every_client_and_releases_cache_after_failure(self):
        first, second = Mock(), Mock()
        first.close.side_effect = OSError("first cleanup failed")
        samples = ResourceSamples({})
        samples.clients = {"first": first, "second": second}
        with self.assertRaisesRegex(ExceptionGroup, "cleanup"):
            samples.close()
        first.close.assert_called_once()
        second.close.assert_called_once()
        samples.close()
        first.close.assert_called_once()

    def test_sns_and_eventbridge_route_to_owned_queue_and_clean_up(self):
        for resource in ("sns", "eventbridge"):
            with self.subTest(resource=resource):
                client = Mock()
                client.create_queue.return_value = {"QueueUrl": "http://localhost:10195/123/test"}
                client.get_queue_attributes.return_value = {"Attributes": {"QueueArn": "arn:aws:sqs:us-east-1:123:test"}}
                client.create_topic.return_value = {"TopicArn": "arn:aws:sns:us-east-1:123:test"}
                client.put_targets.return_value = {"FailedEntryCount": 0}
                bodies = []
                client.publish.side_effect = lambda **kwargs: bodies.append(json.dumps({"Message": kwargs["Message"]}))
                def put_events(**kwargs):
                    bodies.append(json.dumps({"id": "unrelated-eventbridge-envelope-id", "detail": json.loads(kwargs["Entries"][0]["Detail"])}))
                    return {"FailedEntryCount": 0}
                client.put_events.side_effect = put_events
                client.receive_message.side_effect = lambda **kwargs: {"Messages": [{"Body": bodies[-1], "ReceiptHandle": "receipt"}]}
                samples = ResourceSamples(CREDENTIALS, {name: lambda: client for name in ("sqs", "sns", "events")})
                self.assertEqual("python", samples.run(resource)["language"])
                client.delete_queue.assert_called_once()
                (client.delete_topic if resource == "sns" else client.delete_rule).assert_called_once()

    def test_eventbridge_rejected_target_removes_rule_and_queue(self):
        client = Mock()
        client.create_queue.return_value = {"QueueUrl": "http://localhost:10195/123/test"}
        client.get_queue_attributes.return_value = {"Attributes": {"QueueArn": "arn:aws:sqs:us-east-1:123:test"}}
        client.put_targets.return_value = {"FailedEntryCount": 1}
        with self.assertRaisesRegex(RuntimeError, "rejected"):
            ResourceSamples(CREDENTIALS, {"sqs": lambda: client, "events": lambda: client}).run("eventbridge")
        client.remove_targets.assert_called_once()
        client.delete_rule.assert_called_once()
        client.delete_queue.assert_called_once()

    def test_blob_mismatch_removes_owned_container(self):
        container = Mock()
        container.download_blob.return_value.readall.return_value = b"wrong"
        service = Mock()
        service.get_container_client.return_value = container
        settings = {"Resources__AzureStorage__ConnectionString": "DefaultEndpointsProtocol=http;AccountName=test;AccountKey=dGVzdA==;BlobEndpoint=http://localhost:10000/test;QueueEndpoint=http://localhost:10001/test;TableEndpoint=http://localhost:10002/test;"}
        with self.assertRaises(RuntimeError):
            ResourceSamples(settings, {"blob": lambda: service}).run("blob")
        container.delete_container.assert_called_once()

    def test_eventbridge_publish_rejection_cleans_routing_without_receiving(self):
        client = Mock()
        client.create_queue.return_value = {"QueueUrl": "http://localhost:10195/123/test"}
        client.get_queue_attributes.return_value = {"Attributes": {"QueueArn": "arn:aws:sqs:us-east-1:123:test"}}
        client.put_targets.return_value = {"FailedEntryCount": 0}
        client.put_events.return_value = {"FailedEntryCount": 1, "Entries": [{"ErrorCode": "InternalFailure", "ErrorMessage": "provider failure"}]}
        client.receive_message.side_effect = lambda **kwargs: self.fail("Rejected events must not start receiving messages.")
        with self.assertRaisesRegex(RuntimeError, "EventBridge rejected the event"):
            ResourceSamples(CREDENTIALS, {"sqs": lambda: client, "events": lambda: client}).run("eventbridge")
        client.receive_message.assert_not_called()
        client.remove_targets.assert_called_once()
        client.delete_rule.assert_called_once()
        client.delete_queue.assert_called_once()

    def test_messaging_attempts_all_cleanup_after_a_cleanup_failure(self):
        client = Mock()
        client.create_queue.return_value = {"QueueUrl": "http://localhost:10195/123/test"}
        client.get_queue_attributes.return_value = {"Attributes": {"QueueArn": "arn:aws:sqs:us-east-1:123:test"}}
        client.put_targets.return_value = {"FailedEntryCount": 0}
        bodies = []
        def publish(**kwargs):
            bodies.append(json.dumps({"id": "eventbridge-envelope-id", "detail": json.loads(kwargs["Entries"][0]["Detail"])}))
            return {"FailedEntryCount": 0}
        client.put_events.side_effect = publish
        client.receive_message.side_effect = lambda **kwargs: {"Messages": [{"Body": bodies[-1], "ReceiptHandle": "receipt"}]}
        client.remove_targets.side_effect = OSError("target cleanup failed")
        failure = None
        try:
            ResourceSamples(CREDENTIALS, {"sqs": lambda: client, "events": lambda: client}).run("eventbridge")
        except Exception as error:
            failure = error
        client.delete_rule.assert_called_once()
        client.delete_queue.assert_called_once()
        self.assertIsInstance(failure, ExceptionGroup)

    def fake_s3(self, mismatch=False):
        client = Mock()
        body = []
        client.put_object.side_effect = lambda **kwargs: body.append(kwargs["Body"])
        client.get_object.side_effect = lambda **kwargs: {"Body": io.BytesIO(b"wrong" if mismatch else body[-1])}
        return client

    def test_s3_writes_verifies_cleans_up_and_reuses_client(self):
        client = self.fake_s3()
        factory = Mock(return_value=client)
        samples = ResourceSamples(CREDENTIALS, {"s3": factory})
        samples.run("s3")
        samples.run("s3")
        samples.close()
        factory.assert_called_once()
        self.assertEqual(2, client.delete_object.call_count)
        client.close.assert_called_once()

    def test_s3_preserves_read_and_cleanup_failures(self):
        client = self.fake_s3(mismatch=True)
        client.delete_object.side_effect = OSError("cleanup failed")
        with self.assertRaises(ExceptionGroup) as raised:
            ResourceSamples(CREDENTIALS, {"s3": lambda: client}).run("s3")
        self.assertEqual(2, len(raised.exception.exceptions))
        client.delete_object.assert_called_once()

    def test_sqs_roundtrip_removes_temporary_queue(self):
        client = Mock()
        client.create_queue.return_value = {"QueueUrl": "http://localhost:10195/123/test"}
        body = []
        client.send_message.side_effect = lambda **kwargs: body.append(kwargs["MessageBody"])
        client.receive_message.side_effect = lambda **kwargs: {"Messages": [{"Body": body[-1], "ReceiptHandle": "receipt"}]}
        self.assertEqual("python", ResourceSamples(CREDENTIALS, {"sqs": lambda: client}).run("sqs")["language"])
        client.delete_message.assert_called_once()
        client.delete_queue.assert_called_once()

    def test_dynamodb_roundtrip_deletes_own_item(self):
        client = Mock()
        items = []
        client.put_item.side_effect = lambda **kwargs: items.append(kwargs["Item"])
        client.get_item.side_effect = lambda **kwargs: {"Item": items[-1]}
        ResourceSamples(CREDENTIALS, {"dynamodb": lambda: client}).run("dynamodb")
        client.delete_item.assert_called_once()

    def test_sqs_rejects_matching_id_with_corrupted_payload_without_acknowledging(self):
        client = Mock()
        client.create_queue.return_value = {"QueueUrl": "http://localhost:10195/123/test"}
        bodies = []
        client.send_message.side_effect = lambda **kwargs: bodies.append(json.loads(kwargs["MessageBody"]))
        client.receive_message.side_effect = lambda **kwargs: {"Messages": [{"Body": json.dumps(bodies[-1] | {"text": "corrupted"}), "ReceiptHandle": "receipt"}]}
        with self.assertRaisesRegex(RuntimeError, "return"):
            ResourceSamples(CREDENTIALS, {"sqs": lambda: client}).run("sqs")
        client.delete_message.assert_not_called()
        client.delete_queue.assert_called_once()

    def test_seq_emits_structured_event_without_redirects(self):
        client = Mock()
        client.request.return_value = SimpleNamespace(status_code=201)
        result = ResourceSamples({}, {"http": lambda: client}).run("seq")
        arguments = client.request.call_args.kwargs
        self.assertEqual(result["id"], json.loads(arguments["data"])["SampleId"])
        self.assertFalse(arguments["allow_redirects"])
        self.assertFalse(client.trust_env)
        client.close.assert_called_once()

    def test_wiremock_verifies_and_cleans_up_even_when_read_mismatches(self):
        for mismatch in (False, True):
            with self.subTest(mismatch=mismatch):
                client = Mock()
                body = []
                def request(method, url, **kwargs):
                    if method == "POST":
                        body.append(kwargs["json"]["response"]["body"])
                    return SimpleNamespace(status_code=200, text="wrong" if mismatch else body[0])
                client.request.side_effect = request
                samples = ResourceSamples({}, {"http": lambda: client})
                if mismatch:
                    with self.assertRaises(RuntimeError):
                        samples.run("wiremock")
                else:
                    self.assertEqual("python", samples.run("wiremock")["language"])
                self.assertEqual("DELETE", client.request.call_args.args[0])
                client.close.assert_called_once()


class ServerlessTests(unittest.TestCase):
    def test_native_azure_queue_uses_base64_and_language_specific_queue(self):
        import base64
        client = Mock()
        service = Mock()
        service.get_queue_client.return_value = client
        settings = {"Resources__AzureStorage__ConnectionString": "DefaultEndpointsProtocol=http;AccountName=test;AccountKey=dGVzdA==;BlobEndpoint=http://localhost:10000/test;QueueEndpoint=http://localhost:10001/test;TableEndpoint=http://localhost:10002/test;"}
        ResourceSamples(settings, {"queue": lambda: service}).enqueue_work(WORK, "storage-queue")
        service.get_queue_client.assert_called_once_with("toolbox-python-work")
        self.assertEqual(WORK, json.loads(base64.b64decode(client.send_message.call_args.args[0])))

    def test_invalid_work_never_writes_a_receipt(self):
        for value in (WORK | {"language": "node"}, WORK | {"id": "../wrong"}, WORK | {"text": " "}, WORK | {"text": "x" * 1025}):
            with self.subTest(value=value):
                samples = Mock()
                with self.assertRaises(ValueError):
                    consume_work(value, samples)
                samples.write_receipt.assert_not_called()

    def test_dispatch_returns_404_and_400_without_sdk_calls(self):
        samples = Mock()
        self.assertEqual(404, dispatch("unknown", {}, samples)[0])
        self.assertEqual(400, dispatch("s3", {"text": " "}, samples)[0])
        samples.run.assert_not_called()

    def test_api_gateway_v2_success_and_malformed_envelope(self):
        samples = Mock()
        samples.run.return_value = WORK
        self.assertEqual(400, lambda_handler({"body": "{}"}, samples)["statusCode"])
        result = lambda_handler({"version": "2.0", "requestContext": {"http": {"method": "POST"}}, "rawPath": "/samples/s3", "body": json.dumps({"text": "hello"})}, samples)
        self.assertEqual(200, result["statusCode"])
        self.assertEqual(WORK, json.loads(result["body"]))
        samples.run.assert_called_once_with("s3", "hello")

    def test_malformed_api_gateway_shapes_return_400_without_sdk_calls(self):
        for override in ({"requestContext": None}, {"requestContext": {"http": None}}, {"requestContext": []}, {"rawPath": None}, {"rawPath": 123}, {"rawPath": {}}):
            with self.subTest(override=override):
                samples = Mock()
                event = {"version": "2.0", "body": '{"text":"hello"}', "rawPath": "/samples/s3", "requestContext": {"http": {"method": "POST"}}} | override
                self.assertEqual(400, lambda_handler(event, samples)["statusCode"])
                samples.run.assert_not_called()
                samples.write_receipt.assert_not_called()

    def test_base64_api_gateway_envelope_does_not_invoke_sdk(self):
        samples = Mock()
        event = {"version": "2.0", "rawPath": "/samples/s3", "requestContext": {"http": {"method": "POST"}}, "body": '{"text":"hello"}', "isBase64Encoded": True}
        self.assertEqual(400, lambda_handler(event, samples)["statusCode"])
        samples.run.assert_not_called()

    def test_sqs_reports_only_failed_message_ids(self):
        samples = Mock()
        result = lambda_handler({"Records": [{"eventSource": "aws:sqs", "messageId": "good", "body": json.dumps(WORK)}, {"eventSource": "aws:sqs", "messageId": "bad", "body": "invalid"}]}, samples)
        self.assertEqual({"batchItemFailures": [{"itemIdentifier": "bad"}]}, result)
        samples.write_receipt.assert_called_once_with(WORK, "s3")

    def test_function_binding_names_and_function_auth_are_preserved(self):
        import inspect
        import function_app
        functions = function_app.app.get_functions()
        bindings = [json.loads(function.get_function_json())["bindings"] for function in functions]
        for function, group in zip(functions, bindings):
            parameters = inspect.signature(function.get_user_function()).parameters
            self.assertTrue(all(binding["name"] in parameters for binding in group if binding["direction"] == "IN"))
        http = [binding for group in bindings for binding in group if binding["type"] == "httpTrigger"]
        self.assertEqual(2, len(http))
        self.assertTrue(all(binding["authLevel"].lower() == "function" for binding in http))
        self.assertEqual({"queue.python-work"}, {binding["queueName"] for group in bindings for binding in group if binding["type"] == "serviceBusTrigger"})
        self.assertEqual({"toolbox-python-work"}, {binding["queueName"] for group in bindings for binding in group if binding["type"] == "queueTrigger"})


if __name__ == "__main__":
    unittest.main()
