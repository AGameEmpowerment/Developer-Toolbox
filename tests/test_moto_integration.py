"""Opt-in real Moto HTTP SDK checks, isolated from every developer container."""

import contextlib
import importlib.util
import io
import json
import logging
import os
import sys
import unittest
from pathlib import Path
from unittest.mock import patch


AWS = Path(__file__).resolve().parents[1] / "containers/aws"


def load(name):
    spec = importlib.util.spec_from_file_location("moto_integration_" + name, AWS / (name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


@unittest.skipUnless(os.environ.get("TOOLBOX_TEST_MOTO_API") == "1", "Opt in with TOOLBOX_TEST_MOTO_API=1 and install moto[server].")
class MotoApiTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        from moto.server import ThreadedMotoServer
        logging.getLogger("werkzeug").setLevel(logging.ERROR)
        cls.server = ThreadedMotoServer(ip_address="127.0.0.1", port=0, verbose=False)
        cls.server.start()
        host, port = cls.server.get_host_and_port()
        cls.endpoint = f"http://{host}:{port}"
        cls.local = load("local_session")
        with patch.dict(sys.modules, {"local_session": cls.local}):
            cls.init = load("init")

    @classmethod
    def tearDownClass(cls):
        cls.server.stop()

    def setUp(self):
        import requests
        requests.post(self.endpoint + "/moto-api/reset", timeout=5).raise_for_status()
        self.session = self.local.create_session(self.endpoint)
        with contextlib.redirect_stdout(io.StringIO()):
            self.topology = self.init.initialize(self.session)

    def receive(self, name):
        return self.session.client("sqs").receive_message(QueueUrl=self.topology["queue_urls"][name],
            MaxNumberOfMessages=10, WaitTimeSeconds=0).get("Messages", [])

    def test_s3_roundtrip_preserves_objects_during_repeated_initialization(self):
        s3 = self.session.client("s3")
        s3.put_object(Bucket="toolbox-samples", Key="sample.txt", Body=b"sample value")
        with contextlib.redirect_stdout(io.StringIO()):
            self.init.initialize(self.session)
        self.assertEqual(b"sample value", s3.get_object(Bucket="toolbox-samples", Key="sample.txt")["Body"].read())

    def test_sqs_send_receive_delete_and_caller_relative_queue_urls(self):
        sqs = self.session.client("sqs")
        url = self.topology["queue_urls"]["toolbox-work"]
        self.assertTrue(url.startswith(self.endpoint + "/"))
        sent = sqs.send_message(QueueUrl=url, MessageBody="work")
        received = self.receive("toolbox-work")
        self.assertEqual(sent["MessageId"], received[0]["MessageId"])
        sqs.delete_message(QueueUrl=url, ReceiptHandle=received[0]["ReceiptHandle"])
        self.assertEqual([], self.receive("toolbox-work"))

    def test_sns_raw_subscription_delivers_to_sqs_without_duplicate_subscriptions(self):
        sns = self.session.client("sns")
        with contextlib.redirect_stdout(io.StringIO()):
            self.init.initialize(self.session)
        self.assertEqual(1, len(sns.list_subscriptions_by_topic(TopicArn=self.topology["topic_arn"])["Subscriptions"]))
        sns.publish(TopicArn=self.topology["topic_arn"], Message="notification")
        self.assertEqual(["notification"], [message["Body"] for message in self.receive("toolbox-subscriber")])

    def test_eventbridge_routes_only_matching_events_to_the_sqs_target(self):
        events = self.session.client("events")
        for source in ("unmatched.example", "developertoolbox"):
            result = events.put_events(Entries=[{"EventBusName": "toolbox", "Source": source,
                "DetailType": "ExampleWork", "Detail": '{"message":"event value"}'}])
            self.assertEqual(0, result["FailedEntryCount"])
        messages = self.receive("toolbox-events")
        self.assertEqual(1, len(messages))
        event = json.loads(messages[0]["Body"])
        self.assertEqual("developertoolbox", event["source"])
        self.assertEqual({"message": "event value"}, event["detail"])

    def test_selective_acknowledgement_preserves_unacknowledged_messages(self):
        sqs = self.session.client("sqs")
        url = self.topology["queue_urls"]["toolbox-lambda-work"]
        sqs.set_queue_attributes(QueueUrl=url, Attributes={"VisibilityTimeout": "0"})
        successful = sqs.send_message(QueueUrl=url, MessageBody="success")["MessageId"]
        failed = sqs.send_message(QueueUrl=url, MessageBody="retry")["MessageId"]
        messages = self.receive("toolbox-lambda-work")
        self.assertEqual({successful, failed}, {message["MessageId"] for message in messages})
        for message in messages:
            if message["MessageId"] == successful:
                sqs.delete_message(QueueUrl=url, ReceiptHandle=message["ReceiptHandle"])
        self.assertEqual([failed], [message["MessageId"] for message in self.receive("toolbox-lambda-work")])

    def test_repeated_unacknowledged_records_reach_the_dedicated_dlq(self):
        sqs = self.session.client("sqs")
        url = self.topology["queue_urls"]["toolbox-lambda-work"]
        sqs.set_queue_attributes(QueueUrl=url, Attributes={"VisibilityTimeout": "0"})
        sqs.send_message(QueueUrl=url, MessageBody="poison")
        for _ in range(4):
            self.receive("toolbox-lambda-work")
        self.assertEqual(["poison"], [message["Body"] for message in self.receive("toolbox-lambda-dlq")])
        self.assertEqual([], self.receive("toolbox-lambda-work"))


if __name__ == "__main__":
    unittest.main()
