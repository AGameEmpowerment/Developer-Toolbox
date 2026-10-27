package developertoolbox.core;

/** Lambda's HTTP, EventBridge and SQS body contract is shared with the C# examples. */
public final class JsonMessage {
    private JsonMessage() { }
    public static String parse(String body) {
        if (body == null || body.length() > 32768) throw new IllegalArgumentException("A bounded JSON message is required.");
        try {
            var node = WorkItem.JSON.readTree(body);
            if (node == null || !node.isObject() || !node.path("message").isTextual()) throw new IllegalArgumentException("A string message is required.");
            var message = node.get("message").textValue();
            if (message.isBlank() || message.length() > 4096) throw new IllegalArgumentException("Message must contain 1 to 4096 characters.");
            return message;
        } catch (java.io.IOException ex) { throw new IllegalArgumentException("A valid JSON message is required.", ex); }
    }
}
