package developertoolbox.lambda;

import com.amazonaws.services.lambda.runtime.Context;
import com.amazonaws.services.lambda.runtime.RequestHandler;
import developertoolbox.core.JsonMessage;
import developertoolbox.core.WorkItem;
import java.nio.ByteBuffer;
import java.nio.charset.CodingErrorAction;
import java.nio.charset.StandardCharsets;
import java.util.Base64;
import java.util.Map;

/** API Gateway v2 request/response example; also callable through the local Runtime Interface Emulator. */
public final class HttpFunction implements RequestHandler<Map<String, Object>, Map<String, Object>> {
    @Override public Map<String, Object> handleRequest(Map<String, Object> input, Context context) {
        try {
            if (input == null || !(input.get("body") instanceof String body)) throw new IllegalArgumentException("A body is required.");
            if (body.length() > 65536) throw new IllegalArgumentException("A bounded body is required.");
            if (Boolean.TRUE.equals(input.get("isBase64Encoded"))) {
                body = StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT)
                    .decode(ByteBuffer.wrap(Base64.getDecoder().decode(body))).toString();
            }
            var message = JsonMessage.parse(body);
            return response(200, WorkItem.JSON.writeValueAsString(Map.of("message", message)));
        } catch (java.io.IOException | IllegalArgumentException ex) {
            return response(400, "{\"error\":\"Provide JSON with a nonempty message of at most 4096 characters.\"}");
        }
    }
    private static Map<String, Object> response(int status, String body) {
        return Map.of("statusCode", status, "headers", Map.of("Content-Type", "application/json"), "body", body, "isBase64Encoded", false);
    }
}
