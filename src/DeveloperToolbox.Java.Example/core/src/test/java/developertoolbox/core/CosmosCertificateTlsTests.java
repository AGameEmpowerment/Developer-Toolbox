package developertoolbox.core;

import com.sun.net.httpserver.HttpsConfigurator;
import com.sun.net.httpserver.HttpsServer;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import java.net.InetSocketAddress;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.KeyStore;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.TimeUnit;
import javax.net.ssl.HttpsURLConnection;
import javax.net.ssl.KeyManagerFactory;
import javax.net.ssl.SSLContext;
import javax.net.ssl.SSLException;
import static org.junit.jupiter.api.Assertions.*;

class CosmosCertificateTlsTests {
    @TempDir static Path directory;
    private static char[] password;
    private static SSLContext matching, mismatching;

    @BeforeAll
    static void certificates() throws Exception {
        password = UUID.randomUUID().toString().toCharArray();
        matching = serverContext("localhost");
        mismatching = serverContext("wrong.invalid");
    }

    private static SSLContext serverContext(String dnsName) throws Exception {
        var storePath = directory.resolve(dnsName + ".p12");
        var executable = Path.of(System.getProperty("java.home"), "bin",
                System.getProperty("os.name").startsWith("Windows") ? "keytool.exe" : "keytool");
        var process = new ProcessBuilder(executable.toString(), "-genkeypair", "-alias", "fixture",
                "-keyalg", "RSA", "-keysize", "2048", "-validity", "2", "-dname", "CN=" + dnsName,
                "-ext", "SAN=dns:" + dnsName, "-storetype", "PKCS12", "-keystore", storePath.toString(),
                "-storepass", new String(password), "-noprompt")
                .redirectErrorStream(true).redirectOutput(ProcessBuilder.Redirect.DISCARD).start();
        if (!process.waitFor(20, TimeUnit.SECONDS)) {
            process.destroyForcibly();
            process.waitFor(5, TimeUnit.SECONDS);
            fail("The bounded local certificate fixture did not finish.");
        }
        assertEquals(0, process.exitValue(), "The local JDK must generate the synthetic certificate fixture.");
        var store = KeyStore.getInstance("PKCS12");
        try (var input = Files.newInputStream(storePath)) { store.load(input, password); }
        var keys = KeyManagerFactory.getInstance(KeyManagerFactory.getDefaultAlgorithm());
        keys.init(store, password);
        var context = SSLContext.getInstance("TLS");
        context.init(keys.getKeyManagers(), null, null);
        return context;
    }

    private static HttpsServer server(SSLContext context) throws Exception {
        var server = HttpsServer.create(new InetSocketAddress("127.0.0.1", 0), 0);
        server.setHttpsConfigurator(new HttpsConfigurator(context));
        server.createContext("/", exchange -> {
            exchange.sendResponseHeaders(200, -1);
            exchange.close();
        });
        server.start();
        return server;
    }

    @Test
    void importsSelfSignedLocalCertificateWithMatchingSubjectAlternativeName() throws Exception {
        var server = server(matching);
        var defaultVerifier = HttpsURLConnection.getDefaultHostnameVerifier();
        var destination = directory.resolve("matching-trust.jks");
        try {
            var settings = new Settings(Map.of("Resources__Cosmos__Endpoint", "https://localhost:" + server.getAddress().getPort()));
            CosmosCertificate.write(settings, destination, password);
            var trust = KeyStore.getInstance("JKS");
            try (var input = Files.newInputStream(destination)) { trust.load(input, password); }
            assertEquals(1, trust.size());
            assertNotNull(trust.getCertificate("cosmos-emulator-0"));
            assertSame(defaultVerifier, HttpsURLConnection.getDefaultHostnameVerifier());
        } finally { server.stop(0); }
    }

    @Test
    void rejectsLocalServerCertificateForAnotherHostnameWithoutWritingTrustStore() throws Exception {
        var server = server(mismatching);
        var destination = directory.resolve("mismatching-trust.jks");
        try {
            var settings = new Settings(Map.of("Resources__Cosmos__Endpoint", "https://localhost:" + server.getAddress().getPort()));
            assertThrows(SSLException.class, () -> CosmosCertificate.write(settings, destination, password));
            assertFalse(Files.exists(destination));
        } finally { server.stop(0); }
    }
}
