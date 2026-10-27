"""Probe real Moto APIs and the initialized default topology."""

from local_session import create_session


def ready(session):
    """A listening socket alone does not guarantee usable sample resources."""
    try:
        session.client("sqs").get_queue_url(QueueName="toolbox-lambda-work")
        topics = session.client("sns").list_topics().get("Topics", [])
        if not any(topic["TopicArn"].endswith(":toolbox-notifications") for topic in topics):
            return False
        session.client("events").describe_event_bus(Name="toolbox")
        session.client("s3").head_bucket(Bucket="toolbox-samples")
        return True
    except Exception:
        return False


if __name__ == "__main__":
    raise SystemExit(0 if ready(create_session()) else 1)
