package developertoolbox.core;

import com.fasterxml.jackson.databind.ObjectMapper;
import java.util.UUID;

/** The shared C#, Java, Node and Python queue envelope. */
public record WorkItem(String id, String text) {
    public static final ObjectMapper JSON = new ObjectMapper().enable(com.fasterxml.jackson.databind.DeserializationFeature.FAIL_ON_TRAILING_TOKENS);
    public WorkItem {
        if (id == null || id.isBlank() || id.length() > 128 || text == null || text.isBlank() || text.length() > 4096)
            throw new IllegalArgumentException("Work requires an ID up to 128 and text up to 4096 characters.");
    }
    public static WorkItem sample() { return new WorkItem(UUID.randomUUID().toString(), "Java toolbox sample"); }
    public static WorkItem parse(String json) {
        if (json == null || json.length() > 32768) throw new IllegalArgumentException("A bounded JSON work item is required.");
        try {
            var node = JSON.readTree(json);
            if (node == null || !node.isObject() || !node.path("id").isTextual() || !node.path("text").isTextual())
                throw new IllegalArgumentException("JSON id and text must be strings.");
            return new WorkItem(node.get("id").textValue(), node.get("text").textValue());
        } catch (java.io.IOException ex) { throw new IllegalArgumentException("A valid JSON work item is required.", ex); }
    }
    public String json() {
        try { return JSON.writeValueAsString(this); }
        catch (java.io.IOException ex) { throw new IllegalStateException("Cannot serialize work item.", ex); }
    }
}
