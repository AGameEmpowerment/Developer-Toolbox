---
post_title: Free local AWS developer resources
author1: Michael Carey
post_slug: free-local-aws-resources
microsoft_alias: n/a
featured_image: n/a
categories: [documentation]
tags: [aws, moto, rabbitmq, local-development]
ai_note: AI assisted
summary: Shared local S3, SQS, SNS, EventBridge, and RabbitMQ resources for host SDK examples.
post_date: 2026-10-12
---

## Resources

The toolbox runs [Moto](https://docs.getmoto.org/en/latest/docs/server_mode.html)
for S3, SQS, SNS, and EventBridge. Moto is free, open source, and
[Apache-2.0 licensed](https://github.com/getmoto/moto/blob/master/LICENSE),
with no account, authentication token, or paid plan required. The repository
builds the emulator from the official Python image and the official Moto PyPI
package. RabbitMQ provides a separate local broker for RabbitMQ applications.

All resources start in `dev_aws_shared` from `docker-compose-aws.yml`; the setup
scripts start this group alongside the common and Azure groups. They are local developer
examples; Moto implements selected AWS API behavior and does not reproduce
AWS authorization or every production service behavior.

| Resource | Host endpoint or name |
| --- | --- |
| S3, SQS, SNS, EventBridge | `http://localhost:10195` |
| S3 bucket | `toolbox-samples` |
| General SQS work queue | `toolbox-work` |
| General dead-letter queue | `toolbox-dlq` |
| SNS topic / subscriber queue | `toolbox-notifications` / `toolbox-subscriber` |
| EventBridge bus / rule / target queue | `toolbox` / `toolbox-sample` / `toolbox-events` |
| RabbitMQ AMQP / management | `localhost:10190` / `http://localhost:10191` |

Inside the Compose network, the AWS endpoint is `http://aws-emulator:5000`.
The SDK helpers explicitly use dummy `test` credentials and local endpoints,
without reading a developer's AWS profile. Defaults are declared in
[containers/.env.example](../.env.example). Setup creates or extends the ignored
`containers/.env` and generates supported local infrastructure passwords once.

## Startup and state

From the repository root, run:

```powershell
./docker_setup.ps1 -ContainerRuntime docker
# Or select Podman 6 or later:
./docker_setup.ps1 -ContainerRuntime podman
```

```bash
./docker_setup.sh --runtime docker
# Or select Podman 6 or later:
./docker_setup.sh --runtime podman
```

`aws-emulator` initializes the default topology on every process start;
`aws-init` verifies and repeats that initialization during Compose startup.
Repeated initialization preserves current messages, objects, and subscriptions.
Moto keeps state in memory: restarting its container discards custom resources
and data, then restores the default topology. Other toolbox services retain
their documented persistent volumes.

Long-running services use `restart: unless-stopped`; initialization jobs use
`restart: "no"`. A manually stopped service remains stopped until explicitly
started. Running setup is an explicit request to start the whole resource stack
again. The AWS emulator needs no Docker/Podman engine socket.

## SDK and CLI examples

Run the examples on your developer host against the shared resource endpoints.
The [C# guide](../../src/DeveloperToolbox.Example/README.md) covers SDK examples and Entity
Framework samples. [Java](../../src/DeveloperToolbox.Java.Example/README.md),
[Node.js](../../src/DeveloperToolbox.Example/DeveloperToolbox.Example.Node/README.md), and
[Python](../../src/DeveloperToolbox.Example/DeveloperToolbox.Example.Python/README.md) have independent
host build, test, and validation commands. Generate ignored host settings with
`python setup/configure_examples.py` from the repository root.

For AWS CLI checks, use only dummy credentials and the local endpoint:

```bash
export AWS_ACCESS_KEY_ID=test
export AWS_SECRET_ACCESS_KEY=test
export AWS_SESSION_TOKEN=
export AWS_DEFAULT_REGION=us-east-1
aws --endpoint-url http://localhost:10195 s3api list-buckets
aws --endpoint-url http://localhost:10195 sqs list-queues
aws --endpoint-url http://localhost:10195 sns list-topics
aws --endpoint-url http://localhost:10195 events list-event-buses
```

The initialized SNS subscription delivers raw messages to `toolbox-subscriber`.
The EventBridge rule routes events whose source is `developertoolbox` to
`toolbox-events`. Queue policies illustrate sender and source restrictions;
Moto's mock authorization is not a production IAM test.

## Host event-handler examples

Lambda handlers remain sample source code and can be invoked directly from host
code and unit tests with API Gateway, EventBridge, or SQS event payloads. The
shared stack contains no sample application runtimes or SQS dispatchers. Moto
provides messaging and storage APIs; this toolbox does not use it to execute
Lambda handlers or provide managed Lambda event-source polling.

Sending to an SQS queue leaves work available for an explicitly started host
consumer. The language guides describe each handler's payload and receipt
behavior. SQS visibility, acknowledgments, and dead-letter policies can be
exercised through the resource SDKs without bundling application code in images.

## Verification

Fast helper regressions use the standard-library test runner:

```bash
python -m unittest discover -s tests -p 'test_*.py'
```

Actual Moto HTTP checks run an isolated loopback server on an ephemeral port;
they never reset shared containers or contact AWS:

```bash
python -m pip install 'moto[server]==5.2.4'
TOOLBOX_TEST_MOTO_API=1 python -m unittest discover -s tests -p test_moto_integration.py
```

These checks cover the local AWS resource APIs, including S3 roundtrips, SQS
lifecycle, SNS fanout, EventBridge routing, and dead-letter behavior. They do not
claim that Moto runs the sample Lambda handlers. Unit tests exercise handler
validation and partial-batch responses independently of shared infrastructure.

Podman requires a working engine and compatible Compose provider. A host engine
that cannot start containers must be repaired before resource validation;
Compose configuration checks alone do not prove live runtime compatibility.
