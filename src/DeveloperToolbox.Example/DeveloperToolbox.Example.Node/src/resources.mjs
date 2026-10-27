import { randomUUID } from "node:crypto";
import { Agent } from "node:https";
import { S3Client, PutObjectCommand, GetObjectCommand, DeleteObjectCommand } from "@aws-sdk/client-s3";
import { SQSClient, CreateQueueCommand, GetQueueAttributesCommand, SetQueueAttributesCommand, SendMessageCommand, ReceiveMessageCommand, DeleteMessageCommand, DeleteQueueCommand } from "@aws-sdk/client-sqs";
import { SNSClient, CreateTopicCommand, SubscribeCommand, PublishCommand, DeleteTopicCommand } from "@aws-sdk/client-sns";
import { EventBridgeClient, PutRuleCommand, PutTargetsCommand, PutEventsCommand, RemoveTargetsCommand, DeleteRuleCommand } from "@aws-sdk/client-eventbridge";
import { DynamoDBClient, CreateTableCommand, DescribeTableCommand, PutItemCommand, GetItemCommand, DeleteItemCommand } from "@aws-sdk/client-dynamodb";
import { BlobServiceClient, newPipeline, StorageSharedKeyCredential } from "@azure/storage-blob";
import { QueueServiceClient } from "@azure/storage-queue";
import { TableServiceClient, TableClient } from "@azure/data-tables";
import { ServiceBusClient } from "@azure/service-bus";
import { CosmosClient } from "@azure/cosmos";
import { createClient } from "redis";
import amqp from "amqplib";
import nodemailer from "nodemailer";
import { localUrl, storageConnection, serviceBusConnection, workMessage } from "./config.mjs";

const awsTypes = { s3: S3Client, sqs: SQSClient, sns: SNSClient, eventbridge: EventBridgeClient, dynamodb: DynamoDBClient };
export const resourceNames = ["s3", "sqs", "sns", "eventbridge", "dynamodb", "rabbitmq", "servicebus", "blob", "storage-queue", "table", "redis", "cosmos", "seq", "wiremock", "mail"];

export function createLocalBlobClient(settings) {
  const connection = storageConnection(settings);
  const fields = Object.fromEntries(connection.split(";").filter(Boolean).map((part) => {
    const separator = part.indexOf("=");
    return [part.slice(0, separator), part.slice(separator + 1)];
  }));
  const pipeline = newPipeline(new StorageSharedKeyCredential(fields.AccountName, fields.AccountKey), { retryOptions: { maxTries: 1 } });
  // Latest SDKs can lead Azurite. Select its supported API version before signing.
  // https://github.com/Azure/Azurite/blob/main/src/blob/utils/constants.ts
  pipeline.factories.push({ create(next) {
    return { sendRequest(request) {
      request.headers.set("x-ms-version", "2026-06-06");
      return next.sendRequest(request);
    } };
  } });
  return new BlobServiceClient(fields.BlobEndpoint, pipeline);
}

export function createResourceSamples(settings, factories = {}) {
  const clients = new Map();
  function aws(name) {
    const endpoint = name === "dynamodb" ? settings.Resources__DynamoDb__Endpoint || "http://localhost:10180" : settings.Resources__S3__Endpoint || settings.TOOLBOX_AWS_ENDPOINT || "http://localhost:10195";
    localUrl(endpoint, name === "dynamodb" ? ["dynamodb"] : ["aws-emulator"]);
    const accessKeyId = settings.Resources__AWS__AccessKey;
    const secretAccessKey = settings.Resources__AWS__SecretKey;
    if (!accessKeyId || !secretAccessKey) throw new Error("Set the local Resources__AWS__AccessKey and Resources__AWS__SecretKey settings.");
    if (!clients.has(name)) clients.set(name, factories[name]?.() || new awsTypes[name]({ endpoint, region: "us-east-1", credentials: { accessKeyId, secretAccessKey }, forcePathStyle: true, maxAttempts: 1, requestHandler: { connectionTimeout: 5000, requestTimeout: 15000 } }));
    return clients.get(name);
  }
  function storage(name) {
    const connection = storageConnection(settings);
    if (!clients.has(name)) clients.set(name, factories[name]?.() || (name === "blob" ? createLocalBlobClient(settings) : QueueServiceClient.fromConnectionString(connection, { retryOptions: { maxTries: 1 }, keepAliveOptions: { enable: true } })));
    return clients.get(name);
  }
  function bus() {
    const connection = serviceBusConnection(settings);
    if (!clients.has("servicebus")) clients.set("servicebus", factories.servicebus?.() || new ServiceBusClient(connection, { retryOptions: { maxRetries: 0, timeoutInMs: 10000 } }));
    return clients.get("servicebus");
  }
  async function withCleanup(action, cleanup) {
    let failure;
    try { return await action(); } catch (error) { failure = error; throw error; } finally {
      try { await cleanup(AbortSignal.timeout(5000)); } catch (error) {
        if (failure) throw new AggregateError([failure, error], "The sample and its cleanup failed.");
        throw error;
      }
    }
  }
  function equal(actual, expected) {
    if (actual !== expected) throw new Error("The resource did not return the value written by this sample.");
  }
  async function receive(queue, expected, signal) {
    const client = aws("sqs");
    const deadline = Date.now() + 15000;
    while (Date.now() < deadline) {
      signal.throwIfAborted();
      const response = await client.send(new ReceiveMessageCommand({ QueueUrl: queue, WaitTimeSeconds: 1, MaxNumberOfMessages: 1 }), { abortSignal: signal });
      const message = response.Messages?.[0];
      if (message) {
        let value = JSON.parse(message.Body);
        if (value.Message) value = JSON.parse(value.Message);
        if (value.detail) value = value.detail;
        equal(JSON.stringify(workMessage(value)), JSON.stringify(expected));
        await client.send(new DeleteMessageCommand({ QueueUrl: queue, ReceiptHandle: message.ReceiptHandle }), { abortSignal: signal });
        return;
      }
    }
    throw new Error("The sample message was not delivered before the deadline.");
  }
  async function queue(signal) {
    const response = await aws("sqs").send(new CreateQueueCommand({ QueueName: `toolbox-node-validation-${randomUUID()}` }), { abortSignal: signal });
    localUrl(response.QueueUrl, ["aws-emulator"], { path: true });
    return response.QueueUrl;
  }
  async function route(resource, data, signal) {
    const sqs = aws("sqs");
    const url = await queue(signal);
    let topic;
    let rule;
    return withCleanup(async () => {
      if (resource === "sqs") await sqs.send(new SendMessageCommand({ QueueUrl: url, MessageBody: JSON.stringify(data) }), { abortSignal: signal });
      else {
        const { Attributes } = await sqs.send(new GetQueueAttributesCommand({ QueueUrl: url, AttributeNames: ["QueueArn"] }), { abortSignal: signal });
        const arn = Attributes.QueueArn;
        await sqs.send(new SetQueueAttributesCommand({ QueueUrl: url, Attributes: { Policy: JSON.stringify({ Version: "2012-10-17", Statement: [{ Effect: "Allow", Principal: { Service: resource === "sns" ? "sns.amazonaws.com" : "events.amazonaws.com" }, Action: "sqs:SendMessage", Resource: arn }] }) } }), { abortSignal: signal });
        if (resource === "sns") {
          const client = aws("sns");
          topic = (await client.send(new CreateTopicCommand({ Name: `toolbox-node-${data.id}` }), { abortSignal: signal })).TopicArn;
          await client.send(new SubscribeCommand({ TopicArn: topic, Protocol: "sqs", Endpoint: arn }), { abortSignal: signal });
          await client.send(new PublishCommand({ TopicArn: topic, Message: JSON.stringify(data) }), { abortSignal: signal });
        } else {
          const client = aws("eventbridge");
          rule = `toolbox-node-${data.id}`;
          await client.send(new PutRuleCommand({ Name: rule, EventPattern: JSON.stringify({ source: ["toolbox.node"], detail: { id: [data.id] } }) }), { abortSignal: signal });
          const target = await client.send(new PutTargetsCommand({ Rule: rule, Targets: [{ Id: "queue", Arn: arn }] }), { abortSignal: signal });
          if (target.FailedEntryCount) throw new Error("EventBridge rejected the SQS target.");
          const result = await client.send(new PutEventsCommand({ Entries: [{ Source: "toolbox.node", DetailType: "ToolboxSample", Detail: JSON.stringify(data) }] }), { abortSignal: signal });
          if (result.FailedEntryCount) throw new Error("EventBridge rejected the event.");
        }
      }
      await receive(url, data, signal);
      return data;
    }, async (cleanupSignal) => {
      const failures = [];
      const clean = async (operation) => { try { await operation(); } catch (error) { failures.push(error); } };
      if (topic) await clean(() => aws("sns").send(new DeleteTopicCommand({ TopicArn: topic }), { abortSignal: cleanupSignal }));
      if (rule) {
        await clean(() => aws("eventbridge").send(new RemoveTargetsCommand({ Rule: rule, Ids: ["queue"] }), { abortSignal: cleanupSignal }));
        await clean(() => aws("eventbridge").send(new DeleteRuleCommand({ Name: rule }), { abortSignal: cleanupSignal }));
      }
      await clean(() => sqs.send(new DeleteQueueCommand({ QueueUrl: url }), { abortSignal: cleanupSignal }));
      if (failures.length) throw new AggregateError(failures, "Temporary messaging resources could not be removed.");
    });
  }
  async function writeReceipt(work, target, signal = AbortSignal.timeout(20000)) {
    const data = workMessage(work);
    const body = JSON.stringify(data);
    const key = `receipts/node/${data.id}.json`;
    if (target === "s3") await aws("s3").send(new PutObjectCommand({ Bucket: settings.Resources__S3__Bucket || "toolbox-samples", Key: key, Body: body, ContentType: "application/json" }), { abortSignal: signal });
    else {
      const container = storage("blob").getContainerClient("toolbox-samples");
      await container.createIfNotExists({ abortSignal: signal });
      await container.getBlockBlobClient(key).upload(body, Buffer.byteLength(body), { abortSignal: signal });
    }
    return { ...data, receipt: key };
  }
  async function enqueueWork(work, target, signal = AbortSignal.timeout(20000)) {
    const data = workMessage(work);
    if (target === "servicebus") {
      const sender = bus().createSender("queue.node-work");
      try { await sender.sendMessages({ body: data, messageId: data.id }, { abortSignal: signal }); } finally { await sender.close(); }
    } else if (target === "storage-queue") {
      const client = storage("queue").getQueueClient("toolbox-node-work");
      await client.createIfNotExists({ abortSignal: signal });
      await client.sendMessage(Buffer.from(JSON.stringify(data)).toString("base64"), { abortSignal: signal });
    } else if (target === "sqs") {
      const client = aws("sqs");
      const { GetQueueUrlCommand } = await import("@aws-sdk/client-sqs");
      const response = await client.send(new GetQueueUrlCommand({ QueueName: "toolbox-node-lambda-work" }), { abortSignal: signal });
      localUrl(response.QueueUrl, ["aws-emulator"], { path: true });
      await client.send(new SendMessageCommand({ QueueUrl: response.QueueUrl, MessageBody: JSON.stringify(data) }), { abortSignal: signal });
    } else throw new Error("Unknown work target.");
    return data;
  }
  async function run(resource, text = "Hello from Node.js", { signal = AbortSignal.timeout(25000) } = {}) {
    signal.throwIfAborted();
    if (!resourceNames.includes(resource)) throw new RangeError("Unknown toolbox resource.");
    const data = workMessage({ id: randomUUID(), language: "node", text });
    const body = JSON.stringify(data);
    const name = `toolbox-node-${data.id}`;
    if (["sqs", "sns", "eventbridge"].includes(resource)) return route(resource, data, signal);
    if (resource === "s3") {
      const client = aws("s3");
      const input = { Bucket: settings.Resources__S3__Bucket || "toolbox-samples", Key: `samples/node/${data.id}.json` };
      return withCleanup(async () => {
        await client.send(new PutObjectCommand({ ...input, Body: body }), { abortSignal: signal });
        const result = await client.send(new GetObjectCommand(input), { abortSignal: signal });
        equal(await result.Body.transformToString(), body);
        return data;
      }, (abortSignal) => client.send(new DeleteObjectCommand(input), { abortSignal }));
    }
    if (resource === "blob") {
      const container = storage("blob").getContainerClient(name);
      await container.create({ abortSignal: signal });
      return withCleanup(async () => {
        const blob = container.getBlockBlobClient("sample.json");
        await blob.upload(body, Buffer.byteLength(body), { abortSignal: signal });
        equal((await blob.downloadToBuffer(0, undefined, { abortSignal: signal })).toString(), body);
        return data;
      }, (abortSignal) => container.delete({ abortSignal }));
    }
    if (resource === "storage-queue") {
      const client = storage("queue").getQueueClient(name);
      await client.create({ abortSignal: signal });
      return withCleanup(async () => {
        await client.sendMessage(body, { abortSignal: signal });
        const response = await client.receiveMessages({ numberOfMessages: 1, abortSignal: signal });
        equal(response.receivedMessageItems[0]?.messageText, body);
        return data;
      }, (abortSignal) => client.delete({ abortSignal }));
    }
    if (resource === "table") {
      const connection = storageConnection(settings);
      const tableName = `Node${data.id.replaceAll("-", "")}`;
      const service = factories.tableService?.() || TableServiceClient.fromConnectionString(connection, { allowInsecureConnection: true, retryOptions: { maxRetries: 0 } });
      const client = factories.table?.() || TableClient.fromConnectionString(connection, tableName, { allowInsecureConnection: true, retryOptions: { maxRetries: 0 } });
      await service.createTable(tableName, { abortSignal: signal });
      return withCleanup(async () => {
        await client.createEntity({ partitionKey: "samples", rowKey: data.id, text }, { abortSignal: signal });
        equal((await client.getEntity("samples", data.id, { abortSignal: signal })).text, text);
        return data;
      }, (abortSignal) => service.deleteTable(tableName, { abortSignal }));
    }
    if (resource === "servicebus") {
      const client = bus();
      const sender = client.createSender("queue.node-validation");
      const receiver = client.createReceiver("queue.node-validation");
      const unmatched = [];
      return withCleanup(async () => {
        await sender.sendMessages({ body: data, messageId: data.id }, { abortSignal: signal });
        const deadline = Date.now() + 15000;
        while (Date.now() < deadline) {
          signal.throwIfAborted();
          const messages = await receiver.receiveMessages(1, { maxWaitTimeInMs: 1000, abortSignal: signal });
          for (const message of messages) {
            if (message.messageId === data.id) { equal(message.body.text, text); await receiver.completeMessage(message); return data; }
            unmatched.push(message);
          }
        }
        throw new Error("The Service Bus sample message was not delivered.");
      }, async () => {
        try { for (const message of unmatched) await receiver.abandonMessage(message); } finally { await Promise.all([sender.close(), receiver.close()]); }
      });
    }
    if (resource === "rabbitmq") {
      const host = settings.RABBITMQ_HOST || "localhost";
      localUrl(`amqp://${host}:${settings.RABBITMQ_PORT || "10190"}`, ["rabbitmq"], { protocols: ["amqp:"] });
      if (!settings.RABBITMQ_DEFAULT_USER || !settings.RABBITMQ_DEFAULT_PASS) throw new Error("Set local RabbitMQ credentials.");
      const connection = await (factories.rabbitmq?.() || amqp.connect({ hostname: host, port: Number(settings.RABBITMQ_PORT || 10190), username: settings.RABBITMQ_DEFAULT_USER, password: settings.RABBITMQ_DEFAULT_PASS, heartbeat: 5 }, { timeout: 5000 }));
      let channel;
      return withCleanup(async () => {
        channel = await connection.createConfirmChannel();
        const response = await channel.assertQueue("", { exclusive: true, autoDelete: true });
        channel.sendToQueue(response.queue, Buffer.from(body));
        await channel.waitForConfirms();
        const message = await channel.get(response.queue, { noAck: false });
        equal(message?.content.toString(), body);
        channel.ack(message);
        return data;
      }, async () => { try { if (channel) await channel.close(); } finally { await connection.close(); } });
    }
    if (resource === "dynamodb") {
      const client = aws("dynamodb");
      const table = "ToolboxSamplesNode";
      try { await client.send(new DescribeTableCommand({ TableName: table }), { abortSignal: signal }); } catch (error) {
        if (error.name !== "ResourceNotFoundException") throw error;
        await client.send(new CreateTableCommand({ TableName: table, AttributeDefinitions: [{ AttributeName: "id", AttributeType: "S" }], KeySchema: [{ AttributeName: "id", KeyType: "HASH" }], BillingMode: "PAY_PER_REQUEST" }), { abortSignal: signal });
      }
      const input = { TableName: table, Key: { id: { S: data.id } } };
      return withCleanup(async () => {
        await client.send(new PutItemCommand({ TableName: table, Item: { id: { S: data.id }, text: { S: text } } }), { abortSignal: signal });
        equal((await client.send(new GetItemCommand({ ...input, ConsistentRead: true }), { abortSignal: signal })).Item?.text.S, text);
        return data;
      }, (abortSignal) => client.send(new DeleteItemCommand(input), { abortSignal }));
    }
    if (resource === "redis") {
      const connection = settings.Resources__Redis__ConnectionString || "localhost:10120";
      const endpoint = connection.includes("://") ? connection : `redis://${connection}`;
      localUrl(endpoint, ["redis"], { protocols: ["redis:", "rediss:"] });
      const client = factories.redis?.() || createClient({ url: endpoint, socket: { connectTimeout: 5000, reconnectStrategy: false } });
      client.on("error", () => {}); // Command promises expose failures without an unhandled EventEmitter error.
      await client.connect();
      const commands = client.withCommandOptions({ abortSignal: signal, timeout: 15000 });
      return withCleanup(async () => { await commands.set(name, body, { EX: 60 }); equal(await commands.get(name), body); return data; }, async (cleanupSignal) => { try { await client.withCommandOptions({ abortSignal: cleanupSignal, timeout: 5000 }).del(name); } finally { client.destroy(); } });
    }
    if (resource === "cosmos") {
      const endpoint = settings.Resources__Cosmos__Endpoint || "https://localhost:10081";
      localUrl(endpoint, ["cosmosdb"]);
      if (!settings.Resources__Cosmos__Key) throw new Error("Set the public local Cosmos emulator key.");
      if (!clients.has("cosmos")) clients.set("cosmos", factories.cosmos?.() || new CosmosClient({ endpoint, key: settings.Resources__Cosmos__Key, agent: new Agent({ rejectUnauthorized: settings.Resources__Cosmos__AllowUntrustedLocalCertificate?.toLowerCase() !== "true" }), connectionPolicy: { enableEndpointDiscovery: false, requestTimeout: 15000, retryOptions: { maxRetryAttemptCount: 0 } } }));
      const { database } = await clients.get("cosmos").databases.createIfNotExists({ id: "ToolboxSamples" }, { abortSignal: signal });
      const { container } = await database.containers.createIfNotExists({ id: "NodeSamples", partitionKey: "/id" }, { abortSignal: signal });
      return withCleanup(async () => { await container.items.create(data, { abortSignal: signal }); equal((await container.item(data.id, data.id).read({ abortSignal: signal })).resource?.text, text); return data; }, (abortSignal) => container.item(data.id, data.id).delete({ abortSignal }));
    }
    if (resource === "mail") {
      const host = settings.Resources__Mail__Host || "localhost";
      localUrl(`smtp://${host}:${settings.Resources__Mail__Port || 10130}`, ["smtp4dev"], { protocols: ["smtp:"] });
      const transport = factories.mail?.() || nodemailer.createTransport({ host, port: Number(settings.Resources__Mail__Port || 10130), secure: false, ignoreTLS: true, connectionTimeout: 5000, socketTimeout: 10000 });
      try { await transport.sendMail({ from: "toolbox@localhost", to: "developer@localhost", subject: name, text: body }); return data; } finally { transport.close(); }
    }
    const endpoint = resource === "seq" ? settings.Resources__Seq__Endpoint || "http://localhost:10150" : settings.Resources__WireMock__Endpoint || "http://localhost:10080";
    localUrl(endpoint, resource === "seq" ? ["otlp-seq"] : ["wiremock"]);
    const request = factories.fetch || fetch;
    const checked = async (path, options) => {
      const response = await request(new URL(path, endpoint), { ...options, redirect: "error", signal: options?.signal || signal });
      if (!response.ok) throw new Error(`The local ${resource} endpoint returned HTTP ${response.status}.`);
      return response;
    };
    if (resource === "seq") {
      await checked("/api/events/raw?clef", { method: "POST", headers: { "content-type": "application/vnd.serilog.clef", ...(settings.Resources__Seq__ApiKey ? { "X-Seq-ApiKey": settings.Resources__Seq__ApiKey } : {}) }, body: `${JSON.stringify({ "@t": new Date().toISOString(), "@mt": "Toolbox Node sample {SampleId}", SampleId: data.id, Text: text })}\n` });
      return data;
    }
    await checked("/__admin/mappings", { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ id: data.id, request: { method: "GET", url: `/toolbox-node/${data.id}` }, response: { status: 200, body } }) });
    return withCleanup(async () => { equal(await (await checked(`/toolbox-node/${data.id}`)).text(), body); return data; }, (cleanupSignal) => checked(`/__admin/mappings/${data.id}`, { method: "DELETE", signal: cleanupSignal }));
  }
  async function close() {
    const failures = [];
    for (const client of clients.values()) {
      try {
        if (client.close) await client.close();
        else if (client.dispose) await client.dispose();
        else if (client.destroy) await client.destroy();
      } catch (error) { failures.push(error); }
    }
    clients.clear();
    if (failures.length) throw new AggregateError(failures, "SDK client cleanup failed.");
  }
  return { run, writeReceipt, enqueueWork, close };
}
