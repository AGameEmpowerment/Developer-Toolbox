package developertoolbox.functions;

import com.microsoft.azure.functions.*;
import com.microsoft.azure.functions.annotation.*;
import developertoolbox.core.*;
import java.util.Locale;
import java.util.Map;
import java.util.Optional;

/** HTTP SDK samples and native, commit-before-acknowledgment Azure queue examples. */
public final class ToolboxFunctions {
    private final SampleRunner samples;
    private final MessageStore messages;
    private static final class Services {
        private static final Settings SETTINGS = Settings.fromEnvironment();
        private static final SdkSamples SAMPLES = new SdkSamples(SETTINGS);
        private static final DatabaseSamples DATABASES = new DatabaseSamples(SETTINGS);
    }
    public ToolboxFunctions() { this(Services.SAMPLES, Services.DATABASES); }
    public ToolboxFunctions(SampleRunner samples, MessageStore messages) {
        this.samples = java.util.Objects.requireNonNull(samples); this.messages = java.util.Objects.requireNonNull(messages);
    }
    @FunctionName("JavaResourceSample")
    public HttpResponseMessage resource(
        @HttpTrigger(name = "request", methods = {HttpMethod.POST}, authLevel = AuthorizationLevel.FUNCTION, route = "java/samples/{resource}") HttpRequestMessage<Optional<String>> request,
        @BindingName("resource") String resource, ExecutionContext context) {
        var name = resource == null ? "" : resource.toLowerCase(Locale.ROOT);
        if (!SdkSamples.RESOURCES.contains(name) && !name.equals("storagequeue-trigger"))
            return response(request, HttpStatus.NOT_FOUND, Map.of("error", "Unknown Java resource sample."));
        try {
            String result = samples.run(name);
            return response(request, HttpStatus.OK, Map.of("resource", name, "result", result));
        } catch (Exception ex) {
            context.getLogger().warning("Java resource sample failed (" + ex.getClass().getSimpleName() + ").");
            return response(request, HttpStatus.INTERNAL_SERVER_ERROR, Map.of("error", "Java sample failed; check local resource configuration."));
        }
    }
    private static HttpResponseMessage response(HttpRequestMessage<?> request, HttpStatus status, Map<String, String> value) {
        try { return request.createResponseBuilder(status).header("Content-Type", "application/json").body(WorkItem.JSON.writeValueAsString(value)).build(); }
        catch (java.io.IOException ex) { throw new IllegalStateException("Cannot serialize response.", ex); }
    }
    @FunctionName("JavaServiceBusQueueToSqlServer")
    public void serviceBusQueue(
        @ServiceBusQueueTrigger(name = "message", queueName = "queue.java.functions", connection = "ServiceBusConnection") String body, ExecutionContext context) {
        persist("sqlserver", body, "java-servicebus-queue", context);
    }
    @FunctionName("JavaServiceBusTopicToPostgres")
    public void serviceBusTopic(
        @ServiceBusTopicTrigger(name = "message", topicName = "topic.java.functions", subscriptionName = "subscription.java.functions", connection = "ServiceBusConnection") String body, ExecutionContext context) {
        persist("postgres", body, "java-servicebus-topic", context);
    }
    @FunctionName("JavaStorageQueueToPostgres")
    public void storageQueue(
        @QueueTrigger(name = "message", queueName = "toolbox-java-function-work", connection = "ToolboxAzureStorage") String body, ExecutionContext context) {
        persist("postgres", body, "java-azurite-queue", context);
    }
    private void persist(String database, String body, String source, ExecutionContext context) {
        boolean inserted = messages.save(database, WorkItem.parse(body), source);
        context.getLogger().info("Java queue work committed; inserted=" + inserted + ".");
    }
}
