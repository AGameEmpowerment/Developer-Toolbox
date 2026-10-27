"""Create repeatable, local-only S3, SQS, SNS, and EventBridge sample resources."""

import json
from local_session import create_session, error_code


def initialize(session):
    """Upsert topology without purging messages, objects, or existing queues."""
    sqs, sns, events, s3 = (
        session.client(name) for name in ("sqs", "sns", "events", "s3")
    )
    urls = {}
    arns = {}
    lambda_queues = {name: name.replace("-work", "-dlq") for name in (
        "toolbox-lambda-work", "toolbox-java-lambda-work", "toolbox-node-lambda-work",
        "toolbox-python-lambda-work")}
    for name in ("toolbox-dlq", "toolbox-work", "toolbox-subscriber", "toolbox-events",
                 *lambda_queues, *lambda_queues.values()):
        try:
            urls[name] = sqs.get_queue_url(QueueName=name)["QueueUrl"]
        except Exception as error:
            if error_code(error) not in ("AWS.SimpleQueueService.NonExistentQueue",
                                         "QueueDoesNotExist", "NonExistentQueue"):
                raise
            urls[name] = sqs.create_queue(QueueName=name)["QueueUrl"]
        arns[name] = sqs.get_queue_attributes(
            QueueUrl=urls[name], AttributeNames=["QueueArn"]
        )["Attributes"]["QueueArn"]

    for name in ("toolbox-work", "toolbox-subscriber", "toolbox-events", *lambda_queues):
        attributes = {"RedrivePolicy": json.dumps({
            "deadLetterTargetArn": arns[
                lambda_queues.get(name, "toolbox-dlq")
            ], "maxReceiveCount": "3"
        })}
        if name in lambda_queues:
            # Six times the 30-second Lambda timeout leaves room for retries.
            attributes["VisibilityTimeout"] = "180"
        sqs.set_queue_attributes(QueueUrl=urls[name], Attributes=attributes)

    topic = sns.create_topic(Name="toolbox-notifications")["TopicArn"]
    subscriptions = (
        subscription
        for page in sns.get_paginator("list_subscriptions_by_topic").paginate(TopicArn=topic)
        for subscription in page["Subscriptions"]
    )
    existing = next((sub for sub in subscriptions if sub["Protocol"] == "sqs" and
                     sub["Endpoint"] == arns["toolbox-subscriber"]), None)
    if existing:
        sns.set_subscription_attributes(SubscriptionArn=existing["SubscriptionArn"],
                                        AttributeName="RawMessageDelivery", AttributeValue="true")
    else:
        sns.subscribe(TopicArn=topic, Protocol="sqs", Endpoint=arns["toolbox-subscriber"],
                      Attributes={"RawMessageDelivery": "true"})

    try:
        events.describe_event_bus(Name="toolbox")
    except Exception as error:
        if error_code(error) != "ResourceNotFoundException":
            raise
        events.create_event_bus(Name="toolbox")
    rule = events.put_rule(Name="toolbox-sample", EventBusName="toolbox", State="ENABLED",
                           EventPattern=json.dumps({"source": ["developertoolbox"]}))["RuleArn"]

    for queue, principal, source in (
        ("toolbox-subscriber", "sns.amazonaws.com", topic),
        ("toolbox-events", "events.amazonaws.com", rule),
    ):
        sqs.set_queue_attributes(QueueUrl=urls[queue], Attributes={"Policy": json.dumps({
            "Version": "2012-10-17", "Statement": [{
                "Effect": "Allow", "Principal": {"Service": principal},
                "Action": "sqs:SendMessage", "Resource": arns[queue],
                "Condition": {"ArnEquals": {"aws:SourceArn": source}},
            }]
        })})
    result = events.put_targets(Rule="toolbox-sample", EventBusName="toolbox",
                               Targets=[{"Id": "queue", "Arn": arns["toolbox-events"]}])
    if result.get("FailedEntryCount", 0):
        raise RuntimeError(f"Unable to configure EventBridge targets: {result['FailedEntries']}")
    try:
        s3.head_bucket(Bucket="toolbox-samples")
    except Exception as error:
        if error_code(error) not in ("404", "NoSuchBucket", "NotFound"):
            raise
        # us-east-1 omits LocationConstraint; existing objects are preserved.
        s3.create_bucket(Bucket="toolbox-samples")
    print("Local toolbox AWS resources are ready (S3, SQS, SNS, EventBridge).")
    return {"queue_urls": urls, "queue_arns": arns, "topic_arn": topic,
            "bucket": "toolbox-samples"}


if __name__ == "__main__":
    initialize(create_session())
