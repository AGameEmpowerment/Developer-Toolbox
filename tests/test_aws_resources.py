#!/usr/bin/env python3
"""Exercise AWS sample topology and local-only deployment without an AWS account."""

import contextlib
import importlib.util
import io
import json
import re
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import MagicMock, call, patch


ROOT = Path(__file__).resolve().parents[1]
AWS = ROOT / "containers" / "aws"


def load_script(name):
    spec = importlib.util.spec_from_file_location("toolbox_aws_" + name, AWS / (name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


LOCAL = load_script("local_session")
with patch.dict(sys.modules, {"local_session": LOCAL}):
    INIT = load_script("init")
with patch.dict(sys.modules, {"local_session": LOCAL}):
    HEALTH = load_script("health")


class ServiceError(Exception):
    def __init__(self, code):
        super().__init__(code)
        self.response = {"Error": {"Code": code}}


class Topology:
    """Stateful fake retains messages/objects while SDK configuration changes."""

    def __init__(self):
        self.queues = {}
        self.subscriptions = []
        self.buckets = {}
        self.buses = set()
        self.clients = {name: MagicMock() for name in ("sqs", "sns", "events", "s3")}
        sqs, sns, events, s3 = (self.clients[name] for name in ("sqs", "sns", "events", "s3"))
        sqs.create_queue.side_effect = self.create_queue
        sqs.get_queue_url.side_effect = self.get_queue_url
        sqs.get_queue_attributes.side_effect = lambda **kw: {
            "Attributes": {"QueueArn": "arn:aws:sqs:us-east-1:000000000000:" + kw["QueueUrl"].split("/")[-1]}
        }
        sqs.set_queue_attributes.side_effect = self.set_attributes
        sns.create_topic.return_value = {"TopicArn": "arn:aws:sns:us-east-1:000000000000:toolbox-notifications"}
        sns.get_paginator.return_value.paginate.side_effect = lambda **kw: [
            {"Subscriptions": []}, {"Subscriptions": self.subscriptions.copy()}
        ]
        sns.subscribe.side_effect = self.subscribe
        events.describe_event_bus.side_effect = self.describe_bus
        events.create_event_bus.side_effect = lambda **kw: self.buses.add(kw["Name"])
        events.put_rule.return_value = {"RuleArn": "arn:aws:events:us-east-1:000000000000:rule/toolbox/toolbox-sample"}
        events.put_targets.return_value = {"FailedEntryCount": 0, "FailedEntries": []}
        s3.head_bucket.side_effect = self.head_bucket
        s3.create_bucket.side_effect = lambda **kw: self.buckets.setdefault(kw["Bucket"], {})

    def client(self, name):
        return self.clients[name]

    def create_queue(self, **kwargs):
        name = kwargs["QueueName"]
        self.queues.setdefault(name, {"attributes": {}, "messages": []})
        return {"QueueUrl": "http://aws-emulator:5000/000000000000/" + name}

    def set_attributes(self, **kwargs):
        name = kwargs["QueueUrl"].split("/")[-1]
        self.queues[name]["attributes"].update(kwargs["Attributes"])

    def get_queue_url(self, **kwargs):
        name = kwargs["QueueName"]
        if name not in self.queues:
            raise ServiceError("AWS.SimpleQueueService.NonExistentQueue")
        return {"QueueUrl": "http://aws-emulator:5000/000000000000/" + name}

    def subscribe(self, **kwargs):
        self.subscriptions.append({"Protocol": kwargs["Protocol"], "Endpoint": kwargs["Endpoint"],
                                   "SubscriptionArn": "arn:aws:sns:us-east-1:000000000000:subscription"})
        return {"SubscriptionArn": self.subscriptions[-1]["SubscriptionArn"]}

    def describe_bus(self, **kwargs):
        if kwargs["Name"] not in self.buses:
            raise ServiceError("ResourceNotFoundException")
        return {"Name": kwargs["Name"]}

    def head_bucket(self, **kwargs):
        if kwargs["Bucket"] not in self.buckets:
            raise ServiceError("404")
        return {}


class TopologyTests(unittest.TestCase):
    def initialize(self, topology):
        with contextlib.redirect_stdout(io.StringIO()):
            return INIT.initialize(topology)

    def test_queue_redrive_and_lambda_visibility_are_isolated(self):
        topology = Topology()
        result = self.initialize(topology)
        self.assertEqual(12, len(result["queue_urls"]))
        for name in ("toolbox-work", "toolbox-subscriber", "toolbox-events", "toolbox-lambda-work"):
            attributes = topology.queues[name]["attributes"]
            policy = json.loads(attributes["RedrivePolicy"])
            expected = "toolbox-lambda-dlq" if name == "toolbox-lambda-work" else "toolbox-dlq"
            self.assertEqual(result["queue_arns"][expected], policy["deadLetterTargetArn"])
            self.assertEqual("3", policy["maxReceiveCount"])
        self.assertEqual("180", topology.queues["toolbox-lambda-work"]["attributes"]["VisibilityTimeout"])

    def test_routing_policies_restrict_sender_and_source(self):
        topology = Topology()
        result = self.initialize(topology)
        for name, service, source in (
            ("toolbox-subscriber", "sns.amazonaws.com", result["topic_arn"]),
            ("toolbox-events", "events.amazonaws.com", topology.clients["events"].put_rule.return_value["RuleArn"]),
        ):
            policy = json.loads(topology.queues[name]["attributes"]["Policy"])
            statement = policy["Statement"][0]
            self.assertEqual({"Service": service}, statement["Principal"])
            self.assertEqual("sqs:SendMessage", statement["Action"])
            self.assertEqual(result["queue_arns"][name], statement["Resource"])
            self.assertEqual({"ArnEquals": {"aws:SourceArn": source}}, statement["Condition"])
        rule = topology.clients["events"].put_rule.call_args.kwargs
        self.assertEqual({"source": ["developertoolbox"]}, json.loads(rule["EventPattern"]))
        self.assertEqual("ENABLED", rule["State"])
        topology.clients["events"].put_targets.assert_called_once_with(
            Rule="toolbox-sample", EventBusName="toolbox",
            Targets=[{"Id": "queue", "Arn": result["queue_arns"]["toolbox-events"]}])

    def test_repeat_initialization_retains_data_and_avoids_duplicate_subscriptions(self):
        topology = Topology()
        first = self.initialize(topology)
        topology.queues["toolbox-work"]["messages"].append("keep")
        topology.buckets["toolbox-samples"]["sample.txt"] = "keep"
        second = self.initialize(topology)
        self.assertEqual(first, second)
        self.assertEqual(["keep"], topology.queues["toolbox-work"]["messages"])
        self.assertEqual({"sample.txt": "keep"}, topology.buckets["toolbox-samples"])
        topology.clients["sns"].subscribe.assert_called_once()
        topology.clients["sns"].set_subscription_attributes.assert_called_once_with(
            SubscriptionArn=topology.subscriptions[0]["SubscriptionArn"],
            AttributeName="RawMessageDelivery", AttributeValue="true")
        topology.clients["events"].create_event_bus.assert_called_once_with(Name="toolbox")
        topology.clients["s3"].create_bucket.assert_called_once_with(Bucket="toolbox-samples")
        self.assertEqual(12, topology.clients["sqs"].create_queue.call_count)

    def test_partial_target_failure_does_not_report_success(self):
        topology = Topology()
        topology.clients["events"].put_targets.return_value = {
            "FailedEntryCount": 1, "FailedEntries": [{"TargetId": "queue", "ErrorCode": "InternalException"}]}
        output = io.StringIO()
        with self.assertRaisesRegex(RuntimeError, "Unable to configure EventBridge targets"):
            with contextlib.redirect_stdout(output):
                INIT.initialize(topology)
        self.assertNotIn("are ready", output.getvalue())
        topology.clients["s3"].create_bucket.assert_not_called()
        topology.clients["events"].put_targets.return_value = {"FailedEntryCount": 0}
        self.initialize(topology)
        self.assertEqual(1, len(topology.subscriptions))

    def test_access_denied_is_not_treated_as_missing_bucket(self):
        topology = Topology()
        topology.clients["s3"].head_bucket.side_effect = ServiceError("AccessDenied")
        with self.assertRaisesRegex(ServiceError, "AccessDenied"):
            self.initialize(topology)
        topology.clients["s3"].create_bucket.assert_not_called()


class LocalOnlyTests(unittest.TestCase):
    def test_reject_remote_or_malformed_endpoint_before_loading_credentials(self):
        sdk = SimpleNamespace(Session=MagicMock())
        for endpoint in ("https://sqs.us-east-1.amazonaws.com", "http://localhost.evil:4566",
                         "http://localhost:5000/path", "http://test@localhost:4566",
                         "http://localhost:5000/?remote=aws", "http://aws-emulator:0"):
            with self.subTest(endpoint=endpoint), patch.dict(sys.modules, {"boto3": sdk}):
                with self.assertRaises(ValueError):
                    LOCAL.create_session(endpoint)
        sdk.Session.assert_not_called()

    def test_all_clients_receive_local_endpoint_and_only_test_credentials(self):
        sdk = SimpleNamespace(Session=MagicMock())
        configuration = SimpleNamespace(Config=MagicMock())
        with patch.dict(sys.modules, {"boto3": sdk, "botocore.config": configuration}):
            session = LOCAL.create_session("http://localhost:10195")
            for name in ("s3", "sqs", "sns", "events", "lambda", "iam"):
                session.client(name)
        sdk.Session.assert_called_once_with(aws_access_key_id="test", aws_secret_access_key="test",
                                            region_name="us-east-1")
        configuration.Config.assert_called_once_with(proxies={}, connect_timeout=2, read_timeout=10,
            retries={"mode": "standard", "total_max_attempts": 2}, s3={"addressing_style": "path"})
        self.assertEqual([call(name, endpoint_url="http://localhost:10195", config=configuration.Config.return_value)
                          for name in ("s3", "sqs", "sns", "events", "lambda", "iam")],
                         sdk.Session.return_value.client.call_args_list)

    def test_readiness_probes_seeded_resources_and_propagates_provider_failures(self):
        session = MagicMock()
        clients = {name: MagicMock() for name in ("sqs", "sns", "events", "s3")}
        session.client.side_effect = clients.__getitem__
        clients["sns"].list_topics.return_value = {"Topics": [{"TopicArn": "arn:aws:sns:us-east-1:123:toolbox-notifications"}]}
        self.assertTrue(HEALTH.ready(session))
        clients["sqs"].get_queue_url.assert_called_once_with(QueueName="toolbox-lambda-work")
        clients["events"].describe_event_bus.assert_called_once_with(Name="toolbox")
        clients["s3"].head_bucket.assert_called_once_with(Bucket="toolbox-samples")
        for name, method in (("sqs", "get_queue_url"), ("sns", "list_topics"),
                             ("events", "describe_event_bus"), ("s3", "head_bucket")):
            with self.subTest(provider=name):
                getattr(clients[name], method).side_effect = OSError("fixture unavailable")
                self.assertFalse(HEALTH.ready(session))
                getattr(clients[name], method).side_effect = None
        clients["sns"].list_topics.return_value = {"Topics": []}
        self.assertFalse(HEALTH.ready(session))


class ComposeTests(unittest.TestCase):
    @staticmethod
    def shared_service_text():
        return "\n".join(
            (ROOT / "containers" / f"docker-compose-{group}.yml")
            .read_text(encoding="utf-8").split("\nnetworks:", 1)[0]
            for group in ("common", "azure", "aws"))

    def test_service_membership_and_persistent_volumes_in_separate_groups(self):
        groups = {
            "common": {"redis", "redisinsight", "smtp4dev", "otlp-seq", "wiremock"},
            "azure": {"mssql", "cosmosdb", "azurite", "service-bus"},
            "aws": {"postgres", "dynamodb-volume-init", "dynamodb", "rabbitmq", "aws-emulator", "aws-init"},
        }
        for group, expected in groups.items():
            with self.subTest(group=group):
                compose = (ROOT / "containers" / f"docker-compose-{group}.yml").read_text(encoding="utf-8")
                services = compose.split("services:\n", 1)[1].split("\nnetworks:", 1)[0]
                self.assertEqual(expected, set(re.findall(r"^    ([a-z0-9-]+):$", services, re.MULTILINE)))
                if group != "common":
                    volumes = compose.split("\nvolumes:\n", 1)[1]
                    for volume in re.findall(r"^    ([a-z0-9-]+):$", volumes, re.MULTILINE):
                        self.assertIn(f"name: dev_common_shared_{volume}", volumes)

    def test_manual_stop_policy_without_privileged_runtime_socket(self):
        compose = self.shared_service_text()
        self.assertIn('x-podman:\n    in_pod: false', compose)
        sections = dict(re.findall(r"^    ([a-z0-9-]+):\n(.*?)(?=^    [a-z0-9-]+:|\Z)",
                                   compose.split("\nnetworks:", 1)[0], re.MULTILINE | re.DOTALL))
        for name, content in sections.items():
            policy = '"no"' if name in ("aws-init", "dynamodb-volume-init") else "unless-stopped"
            self.assertIn("restart: " + policy, content, name)
            image = re.search(r"^        image: (.+)$", content, re.MULTILINE)
            if image:
                self.assertRegex(image.group(1), r"^(docker\.io|mcr\.microsoft\.com|ghcr\.io)/")
        self.assertNotIn("localstack", compose.lower())
        self.assertNotIn("docker.sock", compose)
        self.assertNotIn("label=disable", compose)
        self.assertIn("context: ./aws", sections["aws-emulator"])
        dockerfile = (AWS / "Dockerfile").read_text()
        self.assertIn("FROM docker.io/library/python:latest", dockerfile)
        self.assertIn('moto[server]==5.2.4', dockerfile)
        self.assertIn("USER 1000:1000", dockerfile)

    def test_added_services_start_by_default_and_publish_loopback_ports(self):
        compose = self.shared_service_text()
        service_section = compose.split("\nnetworks:", 1)[0]
        sections = dict(re.findall(r"^    ([a-z0-9-]+):\n(.*?)(?=^    [a-z0-9-]+:|\Z)",
                                   service_section, re.MULTILINE | re.DOTALL))
        for name in ("rabbitmq", "aws-emulator", "aws-init", "service-bus", "azurite", "postgres", "mssql"):
            self.assertIn(name, sections)
        for name, content in sections.items():
            self.assertNotIn("profiles:", content, name)
        self.assertNotIn("COMPOSE_PROFILES=", (ROOT / "containers" / ".env.example").read_text(encoding="utf-8"))
        for name in ("rabbitmq", "aws-emulator"):
            ports = re.findall(r'^            - "([^"\n]+:\d+)"', sections[name], re.MULTILINE)
            self.assertTrue(ports)
            self.assertTrue(all(port.startswith("127.0.0.1:") for port in ports))
        self.assertIn('/opt/toolbox/aws/health.py', sections["aws-emulator"])
        self.assertIn('condition: service_healthy', sections["aws-init"])
        self.assertIn('"127.0.0.1:10170:5672"', sections["service-bus"])
        self.assertIn('"127.0.0.1:5672:5672"', sections["service-bus"])

    def test_shared_stack_has_only_infrastructure_and_separate_example_queues(self):
        compose = self.shared_service_text()
        self.assertNotIn('../src/', compose)
        self.assertNotIn('lambda-runtime', compose)
        self.assertNotIn('function-app', compose)
        self.assertNotIn('functions-key-init', compose)
        self.assertNotIn('functions-secrets', compose)
        self.assertNotIn('COPY . .', (AWS / 'Dockerfile').read_text())
        self.assertNotIn('dispatch_lambda.py', (AWS / 'Dockerfile').read_text())
        ignored = (ROOT / "src/DeveloperToolbox.Example/.dockerignore").read_text(encoding="utf-8")
        self.assertIn('**/local.settings*.json', ignored)
        self.assertIn('**/appsettings*.json', ignored)
        self.assertIn('**/.tools', ignored)
        config_text = (ROOT / "containers/service-bus/Config.json").read_text(encoding="utf-8")
        namespace = json.loads(re.sub(r'(?m)^\s*//.*$', '', config_text))["UserConfig"]["Namespaces"][0]
        self.assertEqual({'queue.1', 'queue.functions', 'queue.validation', 'queue.java.functions',
                          'queue.node-work', 'queue.python-work', 'queue.node-validation',
                          'queue.python-validation'}, {queue["Name"] for queue in namespace["Queues"]})
        for language in ('node', 'python'):
            source = ROOT / f'src/DeveloperToolbox.Example/DeveloperToolbox.Example.{language.title()}'
            sdk = source / ('src/resources.mjs' if language == 'node' else 'toolbox_samples/resources.py')
            self.assertIn(f'queue.{language}-validation', sdk.read_text(encoding='utf-8'))
            consumer = source / ('src/functions/work-queue.mjs' if language == 'node' else 'function_app.py')
            self.assertNotIn(f'queue.{language}-validation', consumer.read_text(encoding='utf-8'))
        self.assertEqual({'topic.1', 'topic.functions', 'topic.validation', 'topic.java.functions'}, {topic["Name"] for topic in namespace["Topics"]})
        functions_topic = next(topic for topic in namespace["Topics"] if topic["Name"] == 'topic.functions')
        self.assertEqual(['subscription.functions'], [item["Name"] for item in functions_topic["Subscriptions"]])

    def test_dynamodb_initializer_preserves_volume_and_nonroot_emulator(self):
        compose = (ROOT / "containers" / "docker-compose-aws.yml").read_text(encoding="utf-8")
        initializer = compose.split("    dynamodb-volume-init:\n", 1)[1].split("    dynamodb:\n", 1)[0]
        emulator = compose.split("    dynamodb:\n", 1)[1].split("    rabbitmq:\n", 1)[0]
        self.assertIn('image: docker.io/amazon/dynamodb-local:latest', initializer)
        self.assertIn('user: "0:0"', initializer)
        self.assertIn('chown -R dynamodblocal:dynamodblocal /home/dynamodblocal/data', initializer)
        self.assertNotIn('rm ', initializer)
        self.assertIn('dynamodb-data:/home/dynamodblocal/data', initializer)
        self.assertIn('dynamodb-data:/home/dynamodblocal/data', emulator)
        self.assertNotIn('user:', emulator)
        self.assertIn('condition: service_completed_successfully', emulator)

    def test_servicebus_delivery_counts_fit_emulator_startup_limit(self):
        config_text = (ROOT / "containers/service-bus/Config.json").read_text(encoding="utf-8")
        namespaces = json.loads(re.sub(r'(?m)^\s*//.*$', '', config_text))["UserConfig"]["Namespaces"]
        for namespace in namespaces:
            entities = namespace["Queues"] + [subscription for topic in namespace["Topics"]
                                               for subscription in topic["Subscriptions"]]
            for entity in entities:
                with self.subTest(entity=entity["Name"]):
                    # The live emulator rejects the entire configuration above this limit.
                    self.assertGreaterEqual(entity["Properties"]["MaxDeliveryCount"], 1)
                    self.assertLessEqual(entity["Properties"]["MaxDeliveryCount"], 10)


if __name__ == "__main__":
    unittest.main()
