package developertoolbox.lambda;

import com.amazonaws.services.lambda.runtime.*;
import com.amazonaws.services.lambda.runtime.events.SQSEvent;
import developertoolbox.core.WorkItem;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;
import java.util.*;
import static org.junit.jupiter.api.Assertions.*;

class LambdaTests {
    @Test void httpReturnsJsonSharedContract() throws Exception {
        var response = new HttpFunction().handleRequest(Map.of("body", "{\"message\":\"hello\"}"), new TestContext());
        assertEquals(200, response.get("statusCode")); assertEquals("hello", WorkItem.JSON.readTree(response.get("body").toString()).get("message").textValue());
        assertEquals("application/json", ((Map<?, ?>) response.get("headers")).get("Content-Type"));
    }
    @Test void httpDecodesValidBase64Unicode() throws Exception {
        var body = Base64.getEncoder().encodeToString("{\"message\":\"hello 🌎\"}".getBytes(java.nio.charset.StandardCharsets.UTF_8));
        assertEquals(200, new HttpFunction().handleRequest(Map.of("body", body, "isBase64Encoded", true), new TestContext()).get("statusCode"));
    }
    @Test void httpRejectsInvalidUtf8RatherThanReplacementDecoding() {
        var invalid = Base64.getEncoder().encodeToString(new byte[]{(byte)0xFF});
        assertEquals(400, new HttpFunction().handleRequest(Map.of("body", invalid, "isBase64Encoded", true), new TestContext()).get("statusCode"));
    }
    @ParameterizedTest @ValueSource(strings = {"null", "[]", "{}", "{\"message\":1}", "{\"message\":\" \"}", "invalid"})
    void httpReturns400ForMalformedAndInvalidMessages(String body) { assertEquals(400, new HttpFunction().handleRequest(Map.of("body", body), new TestContext()).get("statusCode")); }
    @Test void eventBridgeReadsDetail() { assertEquals("hello", new EventFunction().handleRequest(Map.of("detail", Map.of("message", "hello")), new TestContext())); }
    @Test void eventBridgeRejectsMissingDetail() { assertThrows(IllegalArgumentException.class, () -> new EventFunction().handleRequest(Map.of(), new TestContext())); }
    @Test void sqsCommitsValidRecordsAndReturnsOnlyIndividualFailures() {
        var calls = new ArrayList<String>();
        var function = new SqsFunction((database, work, source) -> { assertEquals("postgres", database); calls.add(work.id()); if (work.id().equals("db-failure")) throw new IllegalStateException("secret-provider-error"); return false; });
        var context = new TestContext();
        var response = function.handleRequest(event(record("good", "{\"message\":\"hello\"}"), record("invalid", "bad"), record("db-failure", "{\"message\":\"hello\"}")), context);
        assertEquals(List.of("good", "db-failure"), calls);
        assertEquals(List.of("invalid", "db-failure"), response.getBatchItemFailures().stream().map(item -> item.getItemIdentifier()).toList());
        assertFalse(context.logs.toString().contains("secret-provider-error"));
        assertTrue(context.logs.toString().contains("Java SQS work committed."));
        assertTrue(context.logs.toString().contains("retrying."));
    }
    @Test void sqsReturnsFailureWhenCommitLeavesInsufficientResponseBudget() {
        var context = new TestContext();
        var function = new SqsFunction((database, work, source) -> { context.remaining = 1000; return true; });
        var response = function.handleRequest(event(record("committed", "{\"message\":\"hello\"}")), context);
        assertEquals(List.of("committed"), response.getBatchItemFailures().stream().map(item -> item.getItemIdentifier()).toList());
        assertFalse(context.logs.toString().contains("Java SQS work committed."));
    }
    @ParameterizedTest @ValueSource(ints = {0, 1000, 25000})
    void exhaustedBudgetReturnsFailuresWithoutSideEffects(int budget) {
        var context = new TestContext(); context.remaining = budget;
        var function = new SqsFunction((database, work, source) -> { fail("No write without sufficient budget"); return false; });
        var response = function.handleRequest(event(record("id", "{\"message\":\"hello\"}")), context);
        assertEquals("id", response.getBatchItemFailures().getFirst().getItemIdentifier());
    }
    @Test void sqsMissingIdsFailBatchBecausePartialFailureCannotIdentifyRecord() {
        assertThrows(IllegalArgumentException.class, () -> new SqsFunction((database, work, source) -> false).handleRequest(event(record("", "{}")), new TestContext()));
    }
    @Test void sqsNullInputFails() { assertThrows(NullPointerException.class, () -> new SqsFunction((database, work, source) -> false).handleRequest(null, new TestContext())); }
    private SQSEvent event(SQSEvent.SQSMessage... messages) { var event = new SQSEvent(); event.setRecords(List.of(messages)); return event; }
    private SQSEvent.SQSMessage record(String id, String body) { var message = new SQSEvent.SQSMessage(); message.setMessageId(id); message.setBody(body); return message; }
    private static final class TestContext implements Context {
        private int remaining = 30000; private final StringBuilder logs = new StringBuilder();
        public String getAwsRequestId() { return "test"; }
        public String getLogGroupName() { return "test"; }
        public String getLogStreamName() { return "test"; }
        public String getFunctionName() { return "test"; }
        public String getFunctionVersion() { return "1"; }
        public String getInvokedFunctionArn() { return "test"; }
        public CognitoIdentity getIdentity() { return null; }
        public ClientContext getClientContext() { return null; }
        public int getRemainingTimeInMillis() { return remaining; }
        public int getMemoryLimitInMB() { return 512; }
        public LambdaLogger getLogger() { return new LambdaLogger() {
            public void log(String value) { logs.append(value); }
            public void log(byte[] value) { logs.append(new String(value, java.nio.charset.StandardCharsets.UTF_8)); }
        }; }
    }
}
