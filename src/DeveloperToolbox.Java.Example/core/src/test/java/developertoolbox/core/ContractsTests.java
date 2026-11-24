package developertoolbox.core;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Map;
import static org.junit.jupiter.api.Assertions.*;

class ContractsTests {
    @ParameterizedTest @ValueSource(strings = {"localhost", "127.0.0.1", "::1", "mssql"})
    void localHostAllowsOnlyKnownHosts(String host) { assertDoesNotThrow(() -> Settings.localHost(host, "mssql")); }
    @ParameterizedTest @ValueSource(strings = {"example.com", "127.1", "localhost.example.com", "mssql.example.com", "0.0.0.0"})
    void localHostRejectsRemoteAndAmbiguousNames(String host) { assertThrows(IllegalArgumentException.class, () -> Settings.localHost(host, "mssql")); }
    @ParameterizedTest @ValueSource(strings = {"https://example.com", "http://localhost@remote", "file:///tmp", "http://localhost/?token=secret", "http://localhost/#fragment"})
    void endpointGuardRejectsUnsafeUris(String endpoint) { assertThrows(IllegalArgumentException.class, () -> new Settings(Map.of("endpoint", endpoint)).endpoint("endpoint", "", "aws-emulator")); }
    @Test void endpointGuardAcceptsComposeAws() { assertEquals("aws-emulator", new Settings(Map.of()).endpoint("endpoint", "http://aws-emulator:5000", "aws-emulator").getHost()); }
    @Test void credentialsMustBeConfigured() { assertThrows(IllegalArgumentException.class, () -> new Settings(Map.of("password", "auto")).require("password")); }
    @Test void certificateImportRejectsCloudBeforeFilesystemOrNetwork(@TempDir Path directory) {
        var destination = directory.resolve("trust.p12");
        assertThrows(IllegalArgumentException.class, () -> CosmosCertificate.write(new Settings(Map.of("Resources__Cosmos__Endpoint", "https://remote")), destination, "test-password".toCharArray()));
        assertFalse(Files.exists(destination));
    }
    @Test void certificateImportPreservesExistingStore(@TempDir Path directory) throws Exception {
        var destination = directory.resolve("trust.p12"); Files.writeString(destination, "existing");
        assertThrows(IllegalArgumentException.class, () -> CosmosCertificate.write(new Settings(Map.of()), destination, "test-password".toCharArray()));
        assertEquals("existing", Files.readString(destination));
    }
    @Test void dotenvIsDataOnlyAndExpandsPriorAssignments(@TempDir Path directory) throws Exception {
        var file = directory.resolve(".env"); Files.writeString(file, "VALUE=local\nEXPAND=\"${VALUE}:123\"\nLITERAL='${VALUE}'\nCOMMAND=$(whoami)\n");
        var values = Settings.readDotEnv(file);
        assertEquals("local:123", values.get("EXPAND")); assertEquals("${VALUE}", values.get("LITERAL")); assertEquals("$(whoami)", values.get("COMMAND"));
    }
    @Test void dotenvDecodesQuotedCredentialsAndPreservesLiteralDollars(@TempDir Path directory) throws Exception {
        var file = directory.resolve(".env");
        Files.writeString(file, """
            TOOLBOX_TEST_DOTENV_SECRET="p\\\"q\\\\path\\$literal$$tail" # comment
            COPY="${TOOLBOX_TEST_DOTENV_SECRET}"
            SINGLE='p;\"$literal#value' # comment
            export UNQUOTED=embedded#hash # comment
            """);
        var values = Settings.readDotEnv(file);
        assertEquals("p\"q\\path$literal$tail", values.get("TOOLBOX_TEST_DOTENV_SECRET"));
        assertEquals(values.get("TOOLBOX_TEST_DOTENV_SECRET"), values.get("COPY"));
        assertEquals("p;\"$literal#value", values.get("SINGLE"));
        assertEquals("embedded#hash", values.get("UNQUOTED"));
    }
    @Test void dotenvSupportsBomFinalAssignmentsAndEscapedWhitespace(@TempDir Path directory) throws Exception {
        var file = directory.resolve(".env");
        Files.writeString(file, "\uFEFF# local configuration\nVALUE=first\nVALUE=last\nCOPY=$VALUE\nSPACES=\" left\\tline\\nnext\\r right \"\n");
        var values = Settings.readDotEnv(file);
        assertEquals("last", values.get("COPY"));
        assertEquals(" left\tline\nnext\r right ", values.get("SPACES"));
    }
    @ParameterizedTest @ValueSource(strings = {
        "PASSWORD=\"secret-fixture", "PASSWORD='secret-fixture", "PASSWORD=\"secret-fixture\" trailing",
        "invalid-name=secret-fixture", "PASSWORD=secret-fixture\0"
    })
    void dotenvMalformedValuesFailWithoutExposingCredentials(String source, @TempDir Path directory) throws Exception {
        var file = directory.resolve(".env"); Files.writeString(file, source);
        var exception = assertThrows(IllegalArgumentException.class, () -> Settings.readDotEnv(file));
        assertFalse(exception.getMessage().contains("secret-fixture"));
    }
    @ParameterizedTest @ValueSource(strings = {"null", "[]", "{}", "{\"id\":1,\"text\":\"x\"}", "{\"id\":\"x\",\"text\":\" \"}", "malformed"})
    void workEnvelopeRejectsMalformedInput(String json) { assertThrows(IllegalArgumentException.class, () -> WorkItem.parse(json)); }
    @Test void workEnvelopeRoundtripAndBounds() {
        var item = new WorkItem("x".repeat(128), "t".repeat(4096)); assertEquals(item, WorkItem.parse(item.json()));
        assertThrows(IllegalArgumentException.class, () -> new WorkItem("x".repeat(129), "text"));
        assertThrows(IllegalArgumentException.class, () -> new WorkItem("id", "x".repeat(4097)));
    }
    @ParameterizedTest @ValueSource(strings = {"null", "[]", "{}", "{\"message\":1}", "{\"message\":\" \"}", "malformed"})
    void lambdaEnvelopeRejectsMalformedInput(String json) { assertThrows(IllegalArgumentException.class, () -> JsonMessage.parse(json)); }
    @Test void lambdaContractPreservesUnicodeAndBounds() {
        assertEquals("Hello 🌎", JsonMessage.parse("{\"message\":\"Hello 🌎\"}"));
        assertThrows(IllegalArgumentException.class, () -> JsonMessage.parse("{\"message\":\"" + "a".repeat(4097) + "\"}"));
    }
    @ParameterizedTest @ValueSource(strings = {" {}", " trailing", " null"})
    void envelopesRejectTrailingJsonAndGarbage(String suffix) {
        assertThrows(IllegalArgumentException.class, () -> WorkItem.parse("{\"id\":\"id\",\"text\":\"hello\"}" + suffix));
        assertThrows(IllegalArgumentException.class, () -> JsonMessage.parse("{\"message\":\"hello\"}" + suffix));
    }
}
