package developertoolbox.core;

import com.azure.core.util.BinaryData;
import com.azure.cosmos.CosmosClientBuilder;
import com.azure.cosmos.models.CosmosContainerProperties;
import com.azure.cosmos.models.PartitionKey;
import com.azure.data.tables.TableClientBuilder;
import com.azure.data.tables.models.TableEntity;
import com.azure.messaging.servicebus.ServiceBusClientBuilder;
import com.azure.messaging.servicebus.ServiceBusMessage;
import com.azure.storage.blob.BlobServiceClientBuilder;
import com.azure.storage.blob.BlobServiceVersion;
import com.azure.storage.queue.QueueClientBuilder;
import com.azure.storage.queue.QueueServiceVersion;
import com.azure.storage.queue.QueueMessageEncoding;
import com.rabbitmq.client.ConnectionFactory;
import jakarta.mail.Message;
import jakarta.mail.Session;
import jakarta.mail.Transport;
import jakarta.mail.internet.InternetAddress;
import jakarta.mail.internet.MimeMessage;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.time.Duration;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import software.amazon.awssdk.auth.credentials.AwsBasicCredentials;
import software.amazon.awssdk.auth.credentials.StaticCredentialsProvider;
import software.amazon.awssdk.awscore.client.builder.AwsSyncClientBuilder;
import software.amazon.awssdk.awscore.client.builder.AwsClientBuilder;
import software.amazon.awssdk.core.client.config.ClientOverrideConfiguration;
import software.amazon.awssdk.core.sync.RequestBody;
import software.amazon.awssdk.http.urlconnection.UrlConnectionHttpClient;
import software.amazon.awssdk.regions.Region;
import software.amazon.awssdk.services.dynamodb.DynamoDbClient;
import software.amazon.awssdk.services.dynamodb.model.AttributeValue;
import software.amazon.awssdk.services.dynamodb.model.KeyType;
import software.amazon.awssdk.services.dynamodb.model.ScalarAttributeType;
import software.amazon.awssdk.services.eventbridge.EventBridgeClient;
import software.amazon.awssdk.services.eventbridge.model.PutEventsRequestEntry;
import software.amazon.awssdk.services.s3.S3Client;
import software.amazon.awssdk.services.s3.S3Configuration;
import software.amazon.awssdk.services.sns.SnsClient;
import software.amazon.awssdk.services.sqs.SqsClient;

/** Real SDK operations restricted to the local toolbox. Temporary resources are cleaned up. */
public final class SdkSamples implements SampleRunner, AutoCloseable {
    public static final Set<String> RESOURCES = Set.of("sqlserver", "postgres", "cosmos", "dynamodb", "redis", "mail",
        "seq", "wiremock", "blob", "storagequeue", "table", "s3", "rabbitmq", "sqs", "sns", "eventbridge", "servicebus", "servicebus-topic");
    private final Settings settings;
    private final DatabaseSamples databases;
    public SdkSamples(Settings settings) { this.settings = settings; this.databases = new DatabaseSamples(settings); }
    @Override public String run(String resource) throws Exception {
        if (resource == null) throw new IllegalArgumentException("Unknown resource.");
        switch (resource.toLowerCase(Locale.ROOT)) {
            case "sqlserver", "postgres" -> { return databases.demonstrate(resource.toLowerCase(Locale.ROOT)); }
            case "s3" -> s3();
            case "sqs" -> sqs();
            case "sns" -> sns();
            case "eventbridge" -> eventBridge();
            case "dynamodb" -> dynamoDb();
            case "blob" -> blob();
            case "storagequeue" -> storageQueue();
            case "table" -> table();
            case "cosmos" -> cosmos();
            case "redis" -> redis();
            case "rabbitmq" -> rabbitMq();
            case "mail" -> mail();
            case "seq" -> seq();
            case "wiremock" -> wireMock();
            case "servicebus" -> publishServiceBus(false);
            case "servicebus-topic" -> publishServiceBus(true);
            case "storagequeue-trigger" -> publishStorageQueue(WorkItem.sample());
            default -> throw new IllegalArgumentException("Unknown resource.");
        }
        return "Java " + resource.toLowerCase(Locale.ROOT) + " SDK sample succeeded.";
    }
    private URI awsEndpoint() { return settings.endpoint("TOOLBOX_AWS_ENDPOINT", "http://localhost:10195", "aws-emulator"); }
    private <B extends AwsSyncClientBuilder<B, C> & AwsClientBuilder<B, C>, C> B aws(B builder, URI endpoint) {
        return builder.endpointOverride(endpoint).region(Region.US_EAST_1)
            .credentialsProvider(StaticCredentialsProvider.create(AwsBasicCredentials.create("test", "test")))
            .httpClientBuilder(UrlConnectionHttpClient.builder().connectionTimeout(Duration.ofSeconds(5)).socketTimeout(Duration.ofSeconds(10)))
            .overrideConfiguration(ClientOverrideConfiguration.builder().apiCallTimeout(Duration.ofSeconds(20)).apiCallAttemptTimeout(Duration.ofSeconds(10)).build());
    }
    private static String name() { return "java" + UUID.randomUUID().toString().replace("-", ""); }
    private static void check(boolean result) { if (!result) throw new IllegalStateException("SDK roundtrip verification failed."); }
    private void s3() {
        var endpoint = settings.endpoint("Resources__S3__Endpoint", awsEndpoint().toString(), "aws-emulator");
        try (var client = aws(S3Client.builder(), endpoint).serviceConfiguration(S3Configuration.builder().pathStyleAccessEnabled(true).build()).build()) {
            var bucket = name();
            client.createBucket(b -> b.bucket(bucket));
            try {
                var work = WorkItem.sample();
                client.putObject(b -> b.bucket(bucket).key(work.id()), RequestBody.fromString(work.text()));
                try { check(client.getObjectAsBytes(b -> b.bucket(bucket).key(work.id())).asUtf8String().equals(work.text())); }
                finally { client.deleteObject(b -> b.bucket(bucket).key(work.id())); }
            } finally { client.deleteBucket(b -> b.bucket(bucket)); }
        }
    }
    private void sqs() {
        try (var client = aws(SqsClient.builder(), awsEndpoint()).build()) {
            var url = client.createQueue(b -> b.queueName(name())).queueUrl();
            // Moto may return its server-side hostname; never follow an unvalidated queue URL.
            var queueUri = URI.create(url); Settings.localHost(queueUri.getHost(), "aws-emulator");
            try {
                var work = WorkItem.sample(); client.sendMessage(b -> b.queueUrl(url).messageBody(work.json()));
                var messages = client.receiveMessage(b -> b.queueUrl(url).waitTimeSeconds(2).maxNumberOfMessages(1)).messages();
                check(messages.size() == 1 && WorkItem.parse(messages.getFirst().body()).equals(work));
                client.deleteMessage(b -> b.queueUrl(url).receiptHandle(messages.getFirst().receiptHandle()));
            } finally { client.deleteQueue(b -> b.queueUrl(url)); }
        }
    }
    private void sns() {
        try (var client = aws(SnsClient.builder(), awsEndpoint()).build()) {
            var arn = client.createTopic(b -> b.name(name())).topicArn();
            try { check(client.publish(b -> b.topicArn(arn).message(WorkItem.sample().json())).messageId() != null); }
            finally { client.deleteTopic(b -> b.topicArn(arn)); }
        }
    }
    private void eventBridge() {
        try (var client = aws(EventBridgeClient.builder(), awsEndpoint()).build()) {
            var result = client.putEvents(b -> b.entries(PutEventsRequestEntry.builder().source("toolbox.java")
                .detailType("ToolboxSample").detail(WorkItem.sample().json()).eventBusName("default").build()));
            check(result.failedEntryCount() == 0 && result.entries().getFirst().eventId() != null);
        }
    }
    private void dynamoDb() {
        var endpoint = settings.endpoint("Resources__DynamoDb__Endpoint", "http://localhost:10180", "dynamodb", "aws-emulator");
        try (var client = aws(DynamoDbClient.builder(), endpoint).build()) {
            var table = name();
            client.createTable(b -> b.tableName(table).billingMode("PAY_PER_REQUEST")
                .attributeDefinitions(a -> a.attributeName("id").attributeType(ScalarAttributeType.S))
                .keySchema(k -> k.attributeName("id").keyType(KeyType.HASH)));
            try {
                client.waiter().waitUntilTableExists(b -> b.tableName(table));
                var work = WorkItem.sample(); var key = Map.of("id", AttributeValue.fromS(work.id()));
                client.putItem(b -> b.tableName(table).item(Map.of("id", AttributeValue.fromS(work.id()), "text", AttributeValue.fromS(work.text()))));
                check(client.getItem(b -> b.tableName(table).key(key).consistentRead(true)).item().get("text").s().equals(work.text()));
            } finally { client.deleteTable(b -> b.tableName(table)); }
        }
    }
    private String storageConnection() {
        var connection = settings.get("Resources__AzureStorage__ConnectionString", "UseDevelopmentStorage=true");
        var entries = new java.util.HashMap<String, String>();
        for (var part : connection.split(";")) {
            int separator = part.indexOf('='); if (separator < 1) continue;
            var key = part.substring(0, separator).toLowerCase(Locale.ROOT);
            if (entries.put(key, part.substring(separator + 1)) != null) throw new IllegalArgumentException("Duplicate storage setting.");
        }
        if (entries.containsKey("usedevelopmentstorage")) {
            if (!entries.get("usedevelopmentstorage").equalsIgnoreCase("true") || entries.size() != 1)
                throw new IllegalArgumentException("Use the local development storage account.");
        } else {
            if (!"devstoreaccount1".equals(entries.get("accountname"))) throw new IllegalArgumentException("Use the Azurite development account.");
            for (var key : java.util.List.of("blobendpoint", "queueendpoint", "tableendpoint")) {
                var uri = URI.create(java.util.Objects.requireNonNull(entries.get(key), "Azurite endpoints are required."));
                if (!Set.of("http", "https").contains(uri.getScheme()) || uri.getUserInfo() != null) throw new IllegalArgumentException("Invalid local storage endpoint.");
                Settings.localHost(uri.getHost(), "azurite");
            }
        }
        return connection;
    }
    private void blob() {
        var service = new BlobServiceClientBuilder().connectionString(storageConnection()).serviceVersion(BlobServiceVersion.V2026_06_06).buildClient();
        var container = service.createBlobContainer(name());
        try { var blob = container.getBlobClient("sample.txt"); blob.upload(BinaryData.fromString("Java storage sample")); check(blob.downloadContent().toString().equals("Java storage sample")); }
        finally { container.delete(); }
    }
    private void storageQueue() {
        var queue = new QueueClientBuilder().connectionString(storageConnection()).queueName(name()).serviceVersion(QueueServiceVersion.V2026_06_06).buildClient();
        queue.create();
        try {
            var work = WorkItem.sample(); queue.sendMessage(work.json());
            var message = queue.receiveMessage(); check(message != null && WorkItem.parse(message.getBody().toString()).equals(work));
            queue.deleteMessage(message.getMessageId(), message.getPopReceipt());
        } finally { queue.delete(); }
    }
    public void publishStorageQueue(WorkItem work) {
        var queue = new QueueClientBuilder().connectionString(storageConnection()).queueName("toolbox-java-function-work")
            .messageEncoding(QueueMessageEncoding.BASE64).serviceVersion(QueueServiceVersion.V2026_06_06).buildClient();
        queue.createIfNotExists(); queue.sendMessage(work.json());
    }
    private void table() {
        var client = new TableClientBuilder().connectionString(storageConnection()).tableName(name()).buildClient();
        client.createTable();
        try {
            client.createEntity(new TableEntity("java", "sample").addProperty("text", "Java table sample"));
            check(client.getEntity("java", "sample").getProperty("text").equals("Java table sample"));
        } finally { client.deleteTable(); }
    }
    public void publishServiceBus(boolean topic) {
        var connection = settings.require("ServiceBusConnection");
        var entries = new java.util.HashMap<String, String>();
        for (var part : connection.split(";")) {
            int separator = part.indexOf('='); if (separator < 1) continue;
            if (entries.put(part.substring(0, separator).toLowerCase(Locale.ROOT), part.substring(separator + 1)) != null)
                throw new IllegalArgumentException("Duplicate Service Bus setting.");
        }
        var endpoint = URI.create(entries.getOrDefault("endpoint", ""));
        if (!"sb".equals(endpoint.getScheme()) || endpoint.getUserInfo() != null || !"true".equalsIgnoreCase(entries.get("usedevelopmentemulator")))
            throw new IllegalArgumentException("A local Service Bus emulator connection is required.");
        Settings.localHost(endpoint.getHost(), "service-bus");
        var sender = new ServiceBusClientBuilder().connectionString(connection).sender();
        if (topic) sender.topicName("topic.java.functions"); else sender.queueName("queue.java.functions");
        try (var client = sender.buildClient()) { var work = WorkItem.sample(); client.sendMessage(new ServiceBusMessage(work.json()).setMessageId(work.id())); }
    }
    private void cosmos() {
        var endpoint = settings.endpoint("Resources__Cosmos__Endpoint", "https://localhost:10081", "cosmosdb");
        // Java's Cosmos SDK uses the JVM trust store; import the emulator certificate rather than disabling TLS.
        try (var client = new CosmosClientBuilder().endpoint(endpoint.toString()).key(settings.require("Resources__Cosmos__Key"))
            .gatewayMode().endpointDiscoveryEnabled(false).buildClient()) {
            var database = name(); client.createDatabase(database);
            try {
                var db = client.getDatabase(database); db.createContainer(new CosmosContainerProperties("messages", "/id"));
                var work = WorkItem.sample(); var container = db.getContainer("messages");
                container.createItem(Map.of("id", work.id(), "text", work.text()));
                check(container.readItem(work.id(), new PartitionKey(work.id()), Map.class).getItem().get("text").equals(work.text()));
            } finally { client.getDatabase(database).delete(); }
        }
    }
    private void redis() {
        var value = settings.get("Resources__Redis__ConnectionString", "localhost:10120");
        if (!value.matches("[A-Za-z0-9.-]+:[0-9]{1,5}")) throw new IllegalArgumentException("Use a local Redis host:port.");
        var parts = value.split(":"); Settings.localHost(parts[0], "redis");
        try (var client = new redis.clients.jedis.Jedis(parts[0], Integer.parseInt(parts[1]), 10000)) {
            var key = name(); client.setex(key, 30, "Java Redis sample");
            try { check("Java Redis sample".equals(client.get(key))); } finally { client.del(key); }
        }
    }
    private void rabbitMq() throws Exception {
        var host = settings.get("RABBITMQ_HOST", "localhost"); Settings.localHost(host, "rabbitmq");
        var factory = new ConnectionFactory(); factory.useBlockingIo(); factory.setHost(host); factory.setPort(settings.port("RABBITMQ_PORT", 10190));
        factory.setUsername(settings.require("RABBITMQ_DEFAULT_USER")); factory.setPassword(settings.require("RABBITMQ_DEFAULT_PASS"));
        factory.setConnectionTimeout(10000); factory.setAutomaticRecoveryEnabled(false);
        try (var connection = factory.newConnection(); var channel = connection.createChannel()) {
            var queue = channel.queueDeclare().getQueue(); var work = WorkItem.sample();
            channel.basicPublish("", queue, null, work.json().getBytes(java.nio.charset.StandardCharsets.UTF_8));
            var result = channel.basicGet(queue, false);
            check(result != null && WorkItem.parse(new String(result.getBody(), java.nio.charset.StandardCharsets.UTF_8)).equals(work));
            channel.basicAck(result.getEnvelope().getDeliveryTag(), false);
        }
    }
    private void mail() throws Exception {
        var host = settings.get("Resources__Mail__Host", "localhost"); Settings.localHost(host, "smtp4dev");
        var properties = new java.util.Properties(); properties.setProperty("mail.smtp.host", host);
        properties.setProperty("mail.smtp.port", Integer.toString(settings.port("Resources__Mail__Port", 10130)));
        properties.setProperty("mail.smtp.connectiontimeout", "10000"); properties.setProperty("mail.smtp.timeout", "10000");
        var message = new MimeMessage(Session.getInstance(properties)); message.setFrom(new InternetAddress("java@toolbox.local"));
        message.setRecipient(Message.RecipientType.TO, new InternetAddress("developer@toolbox.local"));
        message.setSubject("Java toolbox sample"); message.setText("Synthetic developer toolbox mail."); Transport.send(message);
    }
    private HttpClient http() {
        return HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(5)).followRedirects(HttpClient.Redirect.NEVER)
            .proxy(new java.net.ProxySelector() {
                @Override public java.util.List<java.net.Proxy> select(URI uri) { return java.util.List.of(java.net.Proxy.NO_PROXY); }
                @Override public void connectFailed(URI uri, java.net.SocketAddress address, java.io.IOException ex) { }
            }).build();
    }
    private void seq() throws Exception {
        var endpoint = settings.endpoint("Resources__Seq__Endpoint", "http://localhost:10150", "otlp-seq");
        var request = HttpRequest.newBuilder(endpoint.resolve("/api/events/raw?clef"))
            .timeout(Duration.ofSeconds(10)).header("Content-Type", "application/vnd.serilog.clef")
            .POST(HttpRequest.BodyPublishers.ofString("{\"@t\":\"" + java.time.Instant.now() + "\",\"@mt\":\"Java toolbox sample\"}\n"));
        var key = settings.get("Resources__Seq__ApiKey", ""); if (!key.isEmpty()) request.header("X-Seq-ApiKey", key);
        try (var client = http()) { check(client.send(request.build(), HttpResponse.BodyHandlers.discarding()).statusCode() == 201); }
    }
    private void wireMock() throws Exception {
        var endpoint = settings.endpoint("Resources__WireMock__Endpoint", "http://localhost:10080", "wiremock");
        var id = UUID.randomUUID().toString(); var path = "/java-sample/" + id;
        var payload = WorkItem.JSON.writeValueAsString(Map.of("id", id, "request", Map.of("method", "GET", "url", path),
            "response", Map.of("status", 200, "body", "Java WireMock sample")));
        try (var client = http()) {
            check(client.send(HttpRequest.newBuilder(endpoint.resolve("/__admin/mappings")).timeout(Duration.ofSeconds(10))
                .header("Content-Type", "application/json").POST(HttpRequest.BodyPublishers.ofString(payload)).build(), HttpResponse.BodyHandlers.discarding()).statusCode() == 201);
            try {
                var response = client.send(HttpRequest.newBuilder(endpoint.resolve(path)).timeout(Duration.ofSeconds(10)).GET().build(), HttpResponse.BodyHandlers.ofString());
                check(response.statusCode() == 200 && response.body().equals("Java WireMock sample"));
            } finally {
                var cleanup = client.send(HttpRequest.newBuilder(endpoint.resolve("/__admin/mappings/" + id)).timeout(Duration.ofSeconds(5)).DELETE().build(), HttpResponse.BodyHandlers.discarding());
                check(cleanup.statusCode() >= 200 && cleanup.statusCode() < 300);
            }
        }
    }
    @Override public void close() { databases.close(); }
}
