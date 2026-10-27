import { describe, it, expect, vi } from "vitest";
import { mkdtemp, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { createServer } from "node:http";
import { loadSettings, localUrl, storageConnection, serviceBusConnection, workMessage } from "../src/config.mjs";
import { createResourceSamples, createLocalBlobClient } from "../src/resources.mjs";
import { dispatch, createLambdaHandler, consumeWork } from "../src/handlers.mjs";

const credentials = { Resources__AWS__AccessKey: "test", Resources__AWS__SecretKey: "test" };
const work = { id: "b5117c10-14a7-4bf9-8c13-28e8702a6d77", language: "node", text: "hello" };

it("uses Azurite's supported Blob API version with a signed SDK request", async () => {
  const requests = [];
  const server = createServer((request, response) => {
    requests.push(request.headers);
    response.writeHead(201, { "x-ms-request-id": "fake-request", "x-ms-version": "2026-06-06" });
    response.end();
  });
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  try {
    const endpoint = `http://127.0.0.1:${server.address().port}/test`;
    const connection = `DefaultEndpointsProtocol=http;AccountName=test;AccountKey=dGVzdA==;BlobEndpoint=${endpoint};QueueEndpoint=${endpoint};TableEndpoint=${endpoint};`;
    await createLocalBlobClient({ Resources__AzureStorage__ConnectionString: connection }).getContainerClient("sample").create();
    expect(requests).toHaveLength(1);
    expect(requests[0]["x-ms-version"]).toBe("2026-06-06");
    expect(requests[0].authorization).toMatch(/^SharedKey test:/);
  } finally {
    server.closeAllConnections();
    await new Promise((resolve) => server.close(resolve));
  }
});

describe("local configuration", () => {
  it.each([
    "QueueEndpoint=https://remote.example/;QueueEndpoint=http://localhost:10001/test;",
    "QueueEndpoint=http://localhost:10001/test;queueendpoint=https://remote.example/;",
    " QueueEndpoint=https://remote.example/;QueueEndpoint=http://localhost:10001/test;",
    "QueueEndpoint=http://localhost:10001/test;QueueSecondaryEndpoint=https://remote.example/;",
    "QueueEndpoint=http://localhost:10001/test;malformed;"
  ])("rejects storage settings that an SDK could interpret differently: %s", (queueFields) => {
    const connection = `DefaultEndpointsProtocol=http;AccountName=test;AccountKey=dGVzdA==;BlobEndpoint=http://localhost:10000/test;${queueFields}TableEndpoint=http://localhost:10002/test;`;
    expect(() => storageConnection({ ToolboxAzureStorage: connection })).toThrow(/local/);
  });
  it.each([
    "Endpoint=sb://remote;Endpoint=sb://localhost;UseDevelopmentEmulator=true;",
    "Endpoint=sb://localhost;endpoint=sb://remote;UseDevelopmentEmulator=true;",
    "Endpoint=sb://localhost;UseDevelopmentEmulator=true;UseDevelopmentEmulator=false;"
  ])("rejects ambiguous Service Bus settings: %s", (connection) => expect(() => serviceBusConnection({ ServiceBusConnection: connection })).toThrow(/local/));
  it.each(["https://s3.amazonaws.com", "http://localhost.evil.test", "http://user:secret@localhost", "http://localhost?redirect=cloud", "http://localhost/path", "http://localhost/#fragment", "file:///tmp/test", "ftp://localhost"])("rejects unsafe endpoint %s", (value) => expect(() => localUrl(value)).toThrow(/local/));
  it.each(["http://localhost:10195", "http://127.0.0.2:10195", "http://[::1]:10195", "http://aws-emulator:5000"])("permits local endpoint %s", (value) => expect(localUrl(value, ["aws-emulator"])).toBeInstanceOf(URL));
  it("merges generated settings with literal environment overrides", async () => {
    const folder = await mkdtemp(join(tmpdir(), "toolbox-node-settings-"));
    try {
      const path = join(folder, "settings.json");
      await writeFile(path, JSON.stringify({ Resources__AWS__AccessKey: "file" }));
      expect((await loadSettings(path, { Resources__AWS__AccessKey: "$literal" })).Resources__AWS__AccessKey).toBe("$literal");
    } finally { await rm(folder, { recursive: true }); }
  });
  it("requires full storage endpoints and emulator-only Service Bus", () => {
    expect(() => storageConnection({ ToolboxAzureStorage: "UseDevelopmentStorage=true" })).toThrow(/configure_examples/);
    expect(() => serviceBusConnection({ ServiceBusConnection: "Endpoint=sb://localhost;UseDevelopmentEmulator=false;" })).toThrow(/UseDevelopmentEmulator/);
  });
});

describe("SDK guardrails", () => {
  const cases = [
    ...["s3", "sqs", "sns", "eventbridge"].map((resource) => [resource, { ...credentials, TOOLBOX_AWS_ENDPOINT: "https://sqs.us-east-1.amazonaws.com" }]),
    ["dynamodb", { ...credentials, Resources__DynamoDb__Endpoint: "https://dynamodb.us-east-1.amazonaws.com" }],
    ...["blob", "storage-queue", "table"].map((resource) => [resource, { Resources__AzureStorage__ConnectionString: "DefaultEndpointsProtocol=https;AccountName=cloud;AccountKey=secret;BlobEndpoint=https://cloud.blob.core.windows.net;QueueEndpoint=https://cloud.queue.core.windows.net;TableEndpoint=https://cloud.table.core.windows.net;" }]),
    ["servicebus", { ServiceBusConnection: "Endpoint=sb://cloud.servicebus.windows.net;UseDevelopmentEmulator=true;" }],
    ["rabbitmq", { RABBITMQ_HOST: "cloud.example", RABBITMQ_DEFAULT_USER: "u", RABBITMQ_DEFAULT_PASS: "p" }],
    ["redis", { Resources__Redis__ConnectionString: "cloud.example:6379" }],
    ["cosmos", { Resources__Cosmos__Endpoint: "https://cloud.documents.azure.com", Resources__Cosmos__Key: "key", Resources__Cosmos__AllowUntrustedLocalCertificate: "true" }],
    ["seq", { Resources__Seq__Endpoint: "https://cloud.example" }],
    ["wiremock", { Resources__WireMock__Endpoint: "https://cloud.example" }],
    ["mail", { Resources__Mail__Host: "cloud.example" }]
  ];
  it.each(cases)("rejects cloud %s before constructing a client", async (resource, config) => {
    const factory = vi.fn(() => { throw new Error("Client should not be created"); });
    const names = ["s3", "sqs", "sns", "eventbridge", "dynamodb", "blob", "queue", "table", "tableService", "servicebus", "rabbitmq", "redis", "cosmos", "fetch", "mail"];
    const samples = createResourceSamples(config, Object.fromEntries(names.map((name) => [name, factory])));
    await expect(samples.run(resource)).rejects.toThrow(/local/);
    expect(factory).not.toHaveBeenCalled();
  });
  it("does not fall back to ambient AWS credentials", async () => {
    const factory = vi.fn();
    await expect(createResourceSamples({}, { s3: factory }).run("s3")).rejects.toThrow(/AccessKey/);
    expect(factory).not.toHaveBeenCalled();
  });
  it("honors cancellation before a side effect", async () => {
    const factory = vi.fn();
    const controller = new AbortController(); controller.abort();
    await expect(createResourceSamples(credentials, { s3: factory }).run("s3", "hello", { signal: controller.signal })).rejects.toThrow();
    expect(factory).not.toHaveBeenCalled();
  });
});

describe("SDK write/read/cleanup", () => {
  it("closes remaining clients after a close failure and releases its cache", async () => {
    const first = { ...fakeS3(), destroy: vi.fn(() => { throw new Error("first cleanup failed"); }) };
    const second = { send: vi.fn(async () => ({ QueueUrl: "http://localhost:10195/123/test" })), destroy: vi.fn() };
    const samples = createResourceSamples(credentials, { s3: () => first, sqs: () => second });
    await samples.run("s3");
    await samples.enqueueWork(work, "sqs");
    await expect(samples.close()).rejects.toThrow(/cleanup/);
    expect(second.destroy).toHaveBeenCalledOnce();
    await samples.close();
    expect(first.destroy).toHaveBeenCalledOnce();
  });
  it.each(["sns", "eventbridge"])("routes %s into its own SQS queue and removes routing resources", async (resource) => {
    let body;
    const send = vi.fn(async (command) => {
      const type = command.constructor.name;
      if (type === "CreateQueueCommand") return { QueueUrl: "http://localhost:10195/123/test" };
      if (type === "GetQueueAttributesCommand") return { Attributes: { QueueArn: "arn:aws:sqs:us-east-1:123:test" } };
      if (type === "CreateTopicCommand") return { TopicArn: "arn:aws:sns:us-east-1:123:test" };
      if (type === "PublishCommand") body = JSON.stringify({ Message: command.input.Message });
      if (type === "PutEventsCommand") body = JSON.stringify({ id: "unrelated-eventbridge-envelope-id", detail: JSON.parse(command.input.Entries[0].Detail) });
      if (type === "ReceiveMessageCommand") return { Messages: [{ Body: body, ReceiptHandle: "receipt" }] };
      return { FailedEntryCount: 0 };
    });
    const factory = () => ({ send });
    expect((await createResourceSamples(credentials, { sqs: factory, sns: factory, eventbridge: factory }).run(resource)).language).toBe("node");
    const names = send.mock.calls.map(([command]) => command.constructor.name);
    expect(names).toContain(resource === "sns" ? "DeleteTopicCommand" : "DeleteRuleCommand");
    expect(names.at(-1)).toBe("DeleteQueueCommand");
  });
  it("removes EventBridge routing and queue after a rejected target", async () => {
    const send = vi.fn(async (command) => {
      if (command.constructor.name === "CreateQueueCommand") return { QueueUrl: "http://localhost:10195/123/test" };
      if (command.constructor.name === "GetQueueAttributesCommand") return { Attributes: { QueueArn: "arn:aws:sqs:us-east-1:123:test" } };
      return { FailedEntryCount: command.constructor.name === "PutTargetsCommand" ? 1 : 0 };
    });
    await expect(createResourceSamples(credentials, { sqs: () => ({ send }), eventbridge: () => ({ send }) }).run("eventbridge")).rejects.toThrow(/rejected/);
    expect(send.mock.calls.slice(-3).map(([command]) => command.constructor.name)).toEqual(["RemoveTargetsCommand", "DeleteRuleCommand", "DeleteQueueCommand"]);
  });
  it("fails on EventBridge publish rejection and removes routing resources", async () => {
    const send = vi.fn(async (command) => {
      if (command.constructor.name === "CreateQueueCommand") return { QueueUrl: "http://localhost:10195/123/test" };
      if (command.constructor.name === "GetQueueAttributesCommand") return { Attributes: { QueueArn: "arn:aws:sqs:us-east-1:123:test" } };
      if (command.constructor.name === "ReceiveMessageCommand") throw new Error("Must not receive a rejected event.");
      return { FailedEntryCount: command.constructor.name === "PutEventsCommand" ? 1 : 0 };
    });
    await expect(createResourceSamples(credentials, { sqs: () => ({ send }), eventbridge: () => ({ send }) }).run("eventbridge")).rejects.toThrow("EventBridge rejected the event.");
    expect(send.mock.calls.slice(-3).map(([command]) => command.constructor.name)).toEqual(["RemoveTargetsCommand", "DeleteRuleCommand", "DeleteQueueCommand"]);
  });
  it("attempts every routing cleanup after one cleanup fails", async () => {
    let body;
    const send = vi.fn(async (command) => {
      if (command.constructor.name === "CreateQueueCommand") return { QueueUrl: "http://localhost:10195/123/test" };
      if (command.constructor.name === "GetQueueAttributesCommand") return { Attributes: { QueueArn: "arn:aws:sqs:us-east-1:123:test" } };
      if (command.constructor.name === "PutEventsCommand") body = JSON.stringify({ id: "eventbridge-envelope-id", detail: JSON.parse(command.input.Entries[0].Detail) });
      if (command.constructor.name === "ReceiveMessageCommand") return { Messages: [{ Body: body, ReceiptHandle: "receipt" }] };
      if (command.constructor.name === "RemoveTargetsCommand") throw new Error("target cleanup failed");
      return { FailedEntryCount: 0 };
    });
    const failure = await createResourceSamples(credentials, { sqs: () => ({ send }), eventbridge: () => ({ send }) }).run("eventbridge").catch((error) => error);
    expect(send.mock.calls.slice(-3).map(([command]) => command.constructor.name)).toEqual(["RemoveTargetsCommand", "DeleteRuleCommand", "DeleteQueueCommand"]);
    expect(failure).toBeInstanceOf(AggregateError);
  });
  function fakeS3(mismatch = false, cleanupError = false) {
    let body;
    const send = vi.fn(async (command) => {
      if (command.constructor.name === "PutObjectCommand") body = command.input.Body;
      if (command.constructor.name === "GetObjectCommand") return { Body: { transformToString: async () => mismatch ? "wrong" : body } };
      if (command.constructor.name === "DeleteObjectCommand" && cleanupError) throw new Error("cleanup failed");
      return {};
    });
    return { send, destroy: vi.fn() };
  }
  it("writes, verifies, cleans up S3 and reuses its client", async () => {
    const client = fakeS3(); const factory = vi.fn(() => client);
    const samples = createResourceSamples(credentials, { s3: factory });
    await samples.run("s3"); await samples.run("s3"); await samples.close();
    expect(factory).toHaveBeenCalledTimes(1);
    expect(client.send.mock.calls.map(([command]) => command.constructor.name)).toEqual(["PutObjectCommand", "GetObjectCommand", "DeleteObjectCommand", "PutObjectCommand", "GetObjectCommand", "DeleteObjectCommand"]);
    expect(client.destroy).toHaveBeenCalledOnce();
  });
  it("preserves both read mismatch and cleanup failure", async () => {
    const client = fakeS3(true, true);
    await expect(createResourceSamples(credentials, { s3: () => client }).run("s3")).rejects.toBeInstanceOf(AggregateError);
    expect(client.send.mock.calls.at(-1)[0].constructor.name).toBe("DeleteObjectCommand");
  });
  it("uses a fresh cleanup signal after an SDK cancellation", async () => {
    const controller = new AbortController();
    const client = fakeS3();
    client.send.mockImplementation(async (command, options) => {
      if (command.constructor.name === "PutObjectCommand") { controller.abort(); throw new DOMException("Canceled", "AbortError"); }
      expect(options.abortSignal.aborted).toBe(false);
      return {};
    });
    await expect(createResourceSamples(credentials, { s3: () => client }).run("s3", "hello", { signal: controller.signal })).rejects.toThrow(/Canceled/);
    expect(client.send).toHaveBeenCalledTimes(2);
  });
  it("round trips an isolated SQS queue and removes it", async () => {
    let body;
    const send = vi.fn(async (command) => {
      if (command.constructor.name === "CreateQueueCommand") return { QueueUrl: "http://localhost:10195/123/test" };
      if (command.constructor.name === "SendMessageCommand") body = command.input.MessageBody;
      if (command.constructor.name === "ReceiveMessageCommand") return { Messages: [{ Body: body, ReceiptHandle: "receipt" }] };
      return {};
    });
    expect((await createResourceSamples(credentials, { sqs: () => ({ send }) }).run("sqs")).language).toBe("node");
    expect(send.mock.calls.at(-1)[0].constructor.name).toBe("DeleteQueueCommand");
  });
  it("rejects a matching SQS ID with a corrupted payload without acknowledging it", async () => {
    let data;
    const send = vi.fn(async (command) => {
      if (command.constructor.name === "CreateQueueCommand") return { QueueUrl: "http://localhost:10195/123/test" };
      if (command.constructor.name === "SendMessageCommand") data = JSON.parse(command.input.MessageBody);
      if (command.constructor.name === "ReceiveMessageCommand") return { Messages: [{ Body: JSON.stringify({ ...data, text: "corrupted" }), ReceiptHandle: "receipt" }] };
      return {};
    });
    await expect(createResourceSamples(credentials, { sqs: () => ({ send }) }).run("sqs")).rejects.toThrow(/return/);
    const commands = send.mock.calls.map(([command]) => command.constructor.name);
    expect(commands).not.toContain("DeleteMessageCommand");
    expect(commands.at(-1)).toBe("DeleteQueueCommand");
  });
  it("verifies DynamoDB and deletes its own item", async () => {
    let item;
    const send = vi.fn(async (command) => {
      if (command.constructor.name === "PutItemCommand") item = command.input.Item;
      return command.constructor.name === "GetItemCommand" ? { Item: item } : {};
    });
    await createResourceSamples(credentials, { dynamodb: () => ({ send }) }).run("dynamodb");
    expect(send.mock.calls.at(-1)[0].constructor.name).toBe("DeleteItemCommand");
  });
  it("removes an Azure Blob container after a mismatched read", async () => {
    const config = { Resources__AzureStorage__ConnectionString: "DefaultEndpointsProtocol=http;AccountName=test;AccountKey=dGVzdA==;BlobEndpoint=http://localhost:10000/test;QueueEndpoint=http://localhost:10001/test;TableEndpoint=http://localhost:10002/test;" };
    const container = { create: vi.fn(), delete: vi.fn(), getBlockBlobClient: () => ({ upload: vi.fn(), downloadToBuffer: async () => Buffer.from("wrong") }) };
    await expect(createResourceSamples(config, { blob: () => ({ getContainerClient: () => container }) }).run("blob")).rejects.toThrow(/return/);
    expect(container.delete).toHaveBeenCalledOnce();
    expect(container.delete.mock.calls[0][0].abortSignal.aborted).toBe(false);
  });
  it("holds unmatched Service Bus messages until the matching message completes", async () => {
    let id;
    const events = [];
    const sender = { sendMessages: async (message) => { id = message.messageId; }, close: vi.fn() };
    let calls = 0;
    const foreign = { messageId: "foreign" };
    const receiver = {
      receiveMessages: async () => { events.push("receive"); return ++calls === 1 ? [foreign] : [{ messageId: id, body: { text: "hello" } }]; },
      completeMessage: async () => { events.push("complete"); },
      abandonMessage: async (message) => { expect(message).toBe(foreign); events.push("abandon"); }, close: vi.fn()
    };
    await createResourceSamples({ ServiceBusConnection: "Endpoint=sb://localhost;UseDevelopmentEmulator=true;" }, { servicebus: () => ({ createSender: () => sender, createReceiver: () => receiver }) }).run("servicebus", "hello");
    expect(events).toEqual(["receive", "receive", "complete", "abandon"]);
    expect(sender.close).toHaveBeenCalledOnce(); expect(receiver.close).toHaveBeenCalledOnce();
  });
  it("bounds Redis commands and cleans up mismatched values", async () => {
    const client = { on: vi.fn(), connect: vi.fn(), withCommandOptions: vi.fn(), set: vi.fn(), get: async () => "wrong", del: vi.fn(), destroy: vi.fn() };
    client.withCommandOptions.mockReturnValue(client);
    await expect(createResourceSamples({}, { redis: () => client }).run("redis")).rejects.toThrow(/return/);
    expect(client.withCommandOptions.mock.calls.map(([options]) => options.timeout)).toEqual([15000, 5000]);
    expect(client.del).toHaveBeenCalledOnce(); expect(client.destroy).toHaveBeenCalledOnce();
  });
  it("emits structured Seq events with redirects forbidden", async () => {
    const request = vi.fn(async () => new Response("", { status: 201 }));
    const data = await createResourceSamples({}, { fetch: request }).run("seq");
    const options = request.mock.calls[0][1];
    expect(JSON.parse(options.body).SampleId).toBe(data.id);
    expect(options.redirect).toBe("error");
  });
  it.each([false, true])("verifies WireMock and cleans up even on mismatch=%s", async (mismatch) => {
    let body;
    const request = vi.fn(async (_, options) => {
      if (options.method === "POST") body = JSON.parse(options.body).response.body;
      return new Response(options.method === "DELETE" ? "" : mismatch ? "wrong" : body || "", { status: 200 });
    });
    const result = createResourceSamples({}, { fetch: request }).run("wiremock");
    if (mismatch) await expect(result).rejects.toThrow(/return/); else expect((await result).language).toBe("node");
    expect(request.mock.calls.at(-1)[1].method).toBe("DELETE");
  });
});

describe("serverless contracts", () => {
  it("encodes native Azure Queue work as base64 on its own language queue", async () => {
    const queue = { createIfNotExists: vi.fn(), sendMessage: vi.fn() };
    const service = { getQueueClient: vi.fn(() => queue) };
    const config = { Resources__AzureStorage__ConnectionString: "DefaultEndpointsProtocol=http;AccountName=test;AccountKey=dGVzdA==;BlobEndpoint=http://localhost:10000/test;QueueEndpoint=http://localhost:10001/test;TableEndpoint=http://localhost:10002/test;" };
    await createResourceSamples(config, { queue: () => service }).enqueueWork(work, "storage-queue");
    expect(service.getQueueClient).toHaveBeenCalledWith("toolbox-node-work");
    expect(JSON.parse(Buffer.from(queue.sendMessage.mock.calls[0][0], "base64").toString())).toEqual(work);
  });
  it.each([{ ...work, language: "python" }, { ...work, id: "../wrong" }, { ...work, text: " " }, { ...work, text: "x".repeat(1025) }])("rejects invalid work before writing", async (value) => {
    const samples = { writeReceipt: vi.fn() };
    expect(() => workMessage(value)).toThrow();
    await expect(consumeWork(value, samples)).rejects.toThrow();
    expect(samples.writeReceipt).not.toHaveBeenCalled();
  });
  it("returns HTTP404 and400 without SDK calls", async () => {
    const samples = { run: vi.fn() };
    expect((await dispatch("unknown", {}, samples)).status).toBe(404);
    expect((await dispatch("s3", { text: " " }, samples)).status).toBe(400);
    expect(samples.run).not.toHaveBeenCalled();
  });
  it("dispatches API Gateway v2 and rejects malformed envelopes", async () => {
    const samples = { run: vi.fn(async () => work) };
    const handler = createLambdaHandler(samples);
    expect((await handler({ body: "{}" })).statusCode).toBe(400);
    const response = await handler({ version: "2.0", requestContext: { http: { method: "POST" } }, rawPath: "/samples/s3", body: JSON.stringify({ text: "hello" }) });
    expect(response.statusCode).toBe(200); expect(JSON.parse(response.body)).toEqual(work);
    expect(samples.run).toHaveBeenCalledWith("s3", "hello");
  });
  it("rejects a base64 HTTP envelope without invoking an SDK", async () => {
    const samples = { run: vi.fn() };
    const event = { version: "2.0", rawPath: "/samples/s3", requestContext: { http: { method: "POST" } }, body: '{"text":"hello"}', isBase64Encoded: true };
    expect((await createLambdaHandler(samples)(event)).statusCode).toBe(400);
    expect(samples.run).not.toHaveBeenCalled();
  });
  it.each([{ requestContext: undefined }, { requestContext: { http: undefined } }, { requestContext: [] }, { rawPath: undefined }, { rawPath: 123 }, { rawPath: {} }])("rejects malformed API Gateway shapes without SDK calls", async (override) => {
    const samples = { run: vi.fn(), writeReceipt: vi.fn() };
    const event = { version: "2.0", body: '{"text":"hello"}', rawPath: "/samples/s3", requestContext: { http: { method: "POST" } }, ...override };
    expect((await createLambdaHandler(samples)(event)).statusCode).toBe(400);
    expect(samples.run).not.toHaveBeenCalled(); expect(samples.writeReceipt).not.toHaveBeenCalled();
  });
  it("reports only failed SQS IDs and retains successful receipts", async () => {
    const samples = { writeReceipt: vi.fn(async (value) => value) };
    const result = await createLambdaHandler(samples)({ Records: [{ eventSource: "aws:sqs", messageId: "good", body: JSON.stringify(work) }, { eventSource: "aws:sqs", messageId: "bad", body: "invalid" }] });
    expect(result).toEqual({ batchItemFailures: [{ itemIdentifier: "bad" }] });
    expect(samples.writeReceipt).toHaveBeenCalledWith(work, "s3");
  });
});
