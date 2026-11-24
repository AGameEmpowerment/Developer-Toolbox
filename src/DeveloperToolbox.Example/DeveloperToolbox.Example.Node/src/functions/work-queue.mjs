import { app } from "@azure/functions";
import { consumeWork } from "../handlers.mjs";
import { samples } from "./samples-post.mjs";

app.serviceBusQueue("nodeServiceBusWork", { queueName: "queue.node-work", connection: "ServiceBusConnection", handler: (message) => consumeWork(message, samples) });
app.storageQueue("nodeStorageWork", { queueName: "toolbox-node-work", connection: "ToolboxAzureStorage", handler: (message) => consumeWork(message, samples) });
