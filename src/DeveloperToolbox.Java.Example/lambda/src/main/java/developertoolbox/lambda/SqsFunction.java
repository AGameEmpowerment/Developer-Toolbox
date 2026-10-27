package developertoolbox.lambda;

import com.amazonaws.services.lambda.runtime.Context;
import com.amazonaws.services.lambda.runtime.RequestHandler;
import com.amazonaws.services.lambda.runtime.events.SQSBatchResponse;
import com.amazonaws.services.lambda.runtime.events.SQSEvent;
import developertoolbox.core.*;
import java.util.ArrayList;

/** Standard SQS batches, with per-record retry and idempotent PostgreSQL commits. */
public final class SqsFunction implements RequestHandler<SQSEvent, SQSBatchResponse> {
    private final MessageStore messages;
    private static final class Services { private static final DatabaseSamples DATABASES = new DatabaseSamples(Settings.fromEnvironment()); }
    public SqsFunction() { messages = (database, work, source) -> Services.DATABASES.save(database, work, source); }
    public SqsFunction(MessageStore messages) { this.messages = java.util.Objects.requireNonNull(messages); }
    @Override public SQSBatchResponse handleRequest(SQSEvent input, Context context) {
        java.util.Objects.requireNonNull(input); java.util.Objects.requireNonNull(context); java.util.Objects.requireNonNull(input.getRecords());
        var failures = new ArrayList<SQSBatchResponse.BatchItemFailure>();
        for (var record : input.getRecords()) {
            if (record == null || record.getMessageId() == null || record.getMessageId().isBlank())
                throw new IllegalArgumentException("SQS records require message IDs for partial failure reporting.");
            try {
                // JDBC socket/transaction timeouts are cooperative. Leave enough time for one bounded write and the response.
                if (context.getRemainingTimeInMillis() <= 25000) throw new IllegalStateException("Insufficient write budget.");
                var work = new WorkItem(record.getMessageId(), JsonMessage.parse(record.getBody()));
                messages.save("postgres", work, "java-aws-sqs");
                if (context.getRemainingTimeInMillis() <= 1000) throw new IllegalStateException("Response budget expired.");
                context.getLogger().log("Java SQS work committed.\n");
            } catch (Exception ex) {
                context.getLogger().log("Java SQS sample failed (" + ex.getClass().getSimpleName() + "); retrying.\n");
                failures.add(new SQSBatchResponse.BatchItemFailure(record.getMessageId()));
            }
        }
        return new SQSBatchResponse(failures);
    }
}
