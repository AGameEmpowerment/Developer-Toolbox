package developertoolbox.functions;

import com.microsoft.azure.functions.*;
import developertoolbox.core.*;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;
import java.net.URI;
import java.util.*;
import java.util.logging.Logger;
import static org.junit.jupiter.api.Assertions.*;

class ToolboxFunctionsTests {
    private static final ExecutionContext CONTEXT = new ExecutionContext() {
        public Logger getLogger() { return Logger.getLogger("java-function-tests"); }
        public String getInvocationId() { return "test"; }
        public String getFunctionName() { return "test"; }
    };
    @ParameterizedTest @ValueSource(strings = {"SQLSERVER", "postgres", "cosmos", "dynamodb", "redis", "mail", "seq", "wiremock", "blob", "storagequeue", "table", "s3", "rabbitmq", "sqs", "sns", "eventbridge", "servicebus", "servicebus-topic", "storagequeue-trigger"})
    void dispatchesAllSdkAndNativeQueuePublisherSamples(String resource) throws Exception {
        var called = new ArrayList<String>();
        var function = new ToolboxFunctions(name -> { called.add(name); return "verified"; }, (database, work, source) -> true);
        var response = function.resource(new Request(), resource, CONTEXT);
        assertEquals(200, response.getStatusCode()); assertEquals(List.of(resource.toLowerCase(Locale.ROOT)), called);
        assertEquals("application/json", response.getHeader("Content-Type"));
        assertEquals("verified", WorkItem.JSON.readTree(response.getBody().toString()).get("result").textValue());
    }
    @Test void unknownResourceReturns404WithoutDispatch() {
        var function = new ToolboxFunctions(name -> { fail("Must not dispatch"); return ""; }, (database, work, source) -> true);
        assertEquals(404, function.resource(new Request(), "missing", CONTEXT).getStatusCode());
    }
    @Test void providerFailuresReturnSanitized500() {
        var function = new ToolboxFunctions(name -> { throw new IllegalStateException("private-provider-token"); }, (database, work, source) -> true);
        var response = function.resource(new Request(), "s3", CONTEXT);
        assertEquals(500, response.getStatusCode()); assertFalse(response.getBody().toString().contains("private-provider-token"));
    }
    @ParameterizedTest @ValueSource(strings = {"queue", "topic", "storage"})
    void nativeBindingsPersistValidatedWorkToExpectedDatabase(String trigger) {
        var called = new ArrayList<String>();
        var function = new ToolboxFunctions(name -> "", (database, work, source) -> { called.add(database); assertEquals(new WorkItem("id", "text"), work); return false; });
        invoke(function, trigger, "{\"id\":\"id\",\"text\":\"text\"}");
        assertEquals(List.of(trigger.equals("queue") ? "sqlserver" : "postgres"), called);
    }
    @ParameterizedTest @ValueSource(strings = {"queue", "topic", "storage"})
    void failedCommitPropagatesForNativeTriggerRetry(String trigger) {
        var failure = new IllegalStateException("database unavailable");
        var function = new ToolboxFunctions(name -> "", (database, work, source) -> { throw failure; });
        assertSame(failure, assertThrows(IllegalStateException.class, () -> invoke(function, trigger, "{\"id\":\"id\",\"text\":\"text\"}")));
    }
    @ParameterizedTest @ValueSource(strings = {"null", "{}", "{\"id\":1,\"text\":\"text\"}", "{\"id\":\"id\",\"text\":\" \"}"})
    void malformedQueueWorkDoesNotTouchDatabase(String body) {
        var function = new ToolboxFunctions(name -> "", (database, work, source) -> { fail("No write before validation"); return false; });
        assertThrows(IllegalArgumentException.class, () -> function.serviceBusQueue(body, CONTEXT));
    }
    private void invoke(ToolboxFunctions function, String trigger, String body) {
        switch (trigger) {
            case "queue" -> function.serviceBusQueue(body, CONTEXT);
            case "topic" -> function.serviceBusTopic(body, CONTEXT);
            case "storage" -> function.storageQueue(body, CONTEXT);
            default -> fail("Unknown test trigger");
        }
    }
    private static final class Request implements HttpRequestMessage<Optional<String>> {
        public URI getUri() { return URI.create("http://localhost/api/java/samples/s3"); }
        public HttpMethod getHttpMethod() { return HttpMethod.POST; }
        public Map<String, String> getHeaders() { return Map.of(); }
        public Map<String, String> getQueryParameters() { return Map.of(); }
        public Optional<String> getBody() { return Optional.empty(); }
        public HttpResponseMessage.Builder createResponseBuilder(HttpStatus status) { return new ResponseBuilder(status); }
        public HttpResponseMessage.Builder createResponseBuilder(HttpStatusType status) { return new ResponseBuilder(status); }
    }
    private static final class ResponseBuilder implements HttpResponseMessage.Builder {
        private HttpStatusType status; private Object body; private final Map<String, String> headers = new HashMap<>();
        private ResponseBuilder(HttpStatusType status) { this.status = status; }
        public HttpResponseMessage.Builder status(HttpStatusType value) { status = value; return this; }
        public HttpResponseMessage.Builder header(String key, String value) { headers.put(key, value); return this; }
        public HttpResponseMessage.Builder body(Object value) { body = value; return this; }
        public HttpResponseMessage build() {
            var responseStatus = status; var responseBody = body; var responseHeaders = Map.copyOf(headers);
            return new HttpResponseMessage() {
                public HttpStatusType getStatus() { return responseStatus; }
                public String getHeader(String key) { return responseHeaders.get(key); }
                public Object getBody() { return responseBody; }
            };
        }
    }
}
