package developertoolbox.lambda;

import com.amazonaws.services.lambda.runtime.Context;
import com.amazonaws.services.lambda.runtime.RequestHandler;
import developertoolbox.core.JsonMessage;
import developertoolbox.core.WorkItem;
import java.util.Map;

/** EventBridge envelope validation. No emulator delivery semantics are implied by direct invocation. */
public final class EventFunction implements RequestHandler<Map<String, Object>, String> {
    @Override public String handleRequest(Map<String, Object> input, Context context) {
        if (input == null || !(input.get("detail") instanceof Map)) throw new IllegalArgumentException("An EventBridge detail object is required.");
        try { return JsonMessage.parse(WorkItem.JSON.writeValueAsString(input.get("detail"))); }
        catch (java.io.IOException ex) { throw new IllegalArgumentException("Invalid EventBridge detail.", ex); }
    }
}
