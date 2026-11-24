import { workMessage } from "./config.mjs";
import { resourceNames } from "./resources.mjs";

export async function dispatch(resource, input, samples) {
  if (!resourceNames.includes(resource)) return { status: 404, jsonBody: { error: "Unknown toolbox resource." } };
  if (!input || typeof input.text !== "string" || !input.text.trim() || input.text.length > 1024) return { status: 400, jsonBody: { error: "Supply text of 1–1024 characters." } };
  return { status: 200, jsonBody: await samples.run(resource, input.text) };
}

export async function consumeWork(value, samples, target = "blob") {
  return samples.writeReceipt(workMessage(typeof value === "string" ? JSON.parse(value) : value), target);
}

export function createLambdaHandler(samples) {
  return async function handler(event) {
    if (Array.isArray(event?.Records)) {
      const batchItemFailures = [];
      for (const record of event.Records) {
        if (!record || typeof record.messageId !== "string" || record.eventSource !== "aws:sqs") throw new Error("Expected an SQS event with message IDs.");
        try { await consumeWork(record.body, samples, "s3"); } catch { batchItemFailures.push({ itemIdentifier: record.messageId }); }
      }
      return { batchItemFailures };
    }
    if (event?.version !== "2.0" || typeof event.body !== "string" || typeof event.rawPath !== "string" || event.isBase64Encoded === true || event.requestContext?.http?.method !== "POST") return response(400, { error: "Expected an API Gateway v2 POST with a JSON body." });
    let input;
    try { input = JSON.parse(event.body); } catch { return response(400, { error: "Invalid JSON body." }); }
    if (event.rawPath === "/work") {
      let work;
      try { work = workMessage(input); } catch { return response(400, { error: "Invalid work message." }); }
      return response(200, await samples.writeReceipt(work, "s3"));
    }
    const resource = event.rawPath?.match(/^\/samples\/([a-z0-9-]+)$/)?.[1];
    const result = await dispatch(resource, input, samples);
    return response(result.status, result.jsonBody);
  };
}

function response(statusCode, body) {
  return { statusCode, headers: { "content-type": "application/json" }, body: JSON.stringify(body), isBase64Encoded: false };
}
