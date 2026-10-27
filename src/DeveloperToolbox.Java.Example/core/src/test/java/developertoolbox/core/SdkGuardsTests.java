package developertoolbox.core;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;
import java.util.Map;
import static org.junit.jupiter.api.Assertions.*;

class SdkGuardsTests {
    @org.junit.jupiter.params.ParameterizedTest @org.junit.jupiter.params.provider.ValueSource(ints = {204, 500})
    void wireMockCleanupFailureCannotReportSuccess(int cleanupStatus) throws Exception {
        var server = com.sun.net.httpserver.HttpServer.create(new java.net.InetSocketAddress("127.0.0.1", 0), 0);
        var deleted = new java.util.concurrent.atomic.AtomicBoolean();
        server.createContext("/", exchange -> {
            exchange.getRequestBody().readAllBytes();
            if (exchange.getRequestMethod().equals("DELETE")) {
                deleted.set(true);
                exchange.sendResponseHeaders(cleanupStatus, -1);
            } else if (exchange.getRequestMethod().equals("POST")) exchange.sendResponseHeaders(201, -1);
            else {
                var body = "Java WireMock sample".getBytes(java.nio.charset.StandardCharsets.UTF_8);
                exchange.sendResponseHeaders(200, body.length);
                exchange.getResponseBody().write(body);
            }
            exchange.close();
        });
        server.start();
        try (var samples = new SdkSamples(new Settings(Map.of("Resources__WireMock__Endpoint", "http://127.0.0.1:" + server.getAddress().getPort())))) {
            if (cleanupStatus == 204) assertTrue(samples.run("wiremock").contains("succeeded"));
            else assertThrows(IllegalStateException.class, () -> samples.run("wiremock"));
            assertTrue(deleted.get());
        } finally { server.stop(0); }
    }
    @Test void azureTransportCanInitializeWithoutMixedNettyMajors() {
        assertDoesNotThrow(() -> {
            var group = new io.netty.channel.nio.NioEventLoopGroup(1);
            try { assertNotNull(group.next()); } finally { group.shutdownGracefully().syncUninterruptibly(); }
        });
    }
    @ParameterizedTest @ValueSource(strings = {"s3", "sqs", "sns", "eventbridge"})
    void awsExamplesRejectCloudBeforeCreatingClients(String resource) {
        try (var samples = new SdkSamples(new Settings(Map.of("TOOLBOX_AWS_ENDPOINT", "https://amazonaws.com")))) {
            assertThrows(IllegalArgumentException.class, () -> samples.run(resource));
        }
    }
    @ParameterizedTest @ValueSource(strings = {"blob", "storagequeue", "table"})
    void azureStorageRejectsCloudAccounts(String resource) {
        try (var samples = new SdkSamples(new Settings(Map.of("Resources__AzureStorage__ConnectionString", "AccountName=cloud;AccountKey=secret;")))) {
            assertThrows(IllegalArgumentException.class, () -> samples.run(resource));
        }
    }
    @ParameterizedTest @ValueSource(strings = {"UseDevelopmentStorage=true;DevelopmentStorageProxyUri=http://remote;", "UseDevelopmentStorage=true;UseDevelopmentStorage=true;"})
    void azureStorageRejectsProxyAndDuplicateSettings(String connection) {
        try (var samples = new SdkSamples(new Settings(Map.of("Resources__AzureStorage__ConnectionString", connection)))) {
            assertThrows(IllegalArgumentException.class, () -> samples.run("blob"));
        }
    }
    @ParameterizedTest @ValueSource(strings = {"Endpoint=sb://remote;UseDevelopmentEmulator=true;", "Endpoint=sb://localhost;UseDevelopmentEmulator=false;", "Endpoint=sb://localhost;Endpoint=sb://remote;UseDevelopmentEmulator=true;"})
    void serviceBusRejectsCloudAndAmbiguousConfig(String connection) {
        try (var samples = new SdkSamples(new Settings(Map.of("ServiceBusConnection", connection)))) {
            assertThrows(IllegalArgumentException.class, () -> samples.run("servicebus"));
        }
    }
    @Test void unknownResourceHasNoSideEffects() {
        try (var samples = new SdkSamples(new Settings(Map.of()))) { assertThrows(IllegalArgumentException.class, () -> samples.run("unknown")); }
    }
}
