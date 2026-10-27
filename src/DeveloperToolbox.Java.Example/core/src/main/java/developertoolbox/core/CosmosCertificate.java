package developertoolbox.core;

import java.net.Proxy;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.KeyStore;
import java.security.SecureRandom;
import java.security.cert.X509Certificate;
import javax.net.ssl.HttpsURLConnection;
import javax.net.ssl.SSLContext;
import javax.net.ssl.TrustManager;
import javax.net.ssl.X509TrustManager;

/** Explicit trust bootstrap for the local emulator; ordinary SDK calls continue to validate TLS. */
public final class CosmosCertificate {
    private CosmosCertificate() { }
    public static void write(Settings settings, Path destination, char[] password) throws Exception {
        var endpoint = settings.endpoint("Resources__Cosmos__Endpoint", "https://localhost:10081", "cosmosdb");
        if (!"https".equals(endpoint.getScheme())) throw new IllegalArgumentException("The local Cosmos TLS endpoint is required.");
        if (Files.exists(destination)) throw new IllegalArgumentException("Choose a new trust-store path; existing files are preserved.");
        if (password == null || password.length < 6) throw new IllegalArgumentException("Configure JAVA_TRUSTSTORE_PASSWORD with at least 6 characters.");
        var bootstrap = SSLContext.getInstance("TLS");
        // This connection only obtains the public certificate from a validated local destination.
        bootstrap.init(null, new TrustManager[]{new X509TrustManager() {
            public X509Certificate[] getAcceptedIssuers() { return new X509Certificate[0]; }
            public void checkClientTrusted(X509Certificate[] chain, String authType) { throw new UnsupportedOperationException(); }
            public void checkServerTrusted(X509Certificate[] chain, String authType) throws java.security.cert.CertificateException {
                if (chain == null || chain.length == 0) throw new java.security.cert.CertificateException("A certificate is required.");
            }
        }}, new SecureRandom());
        var connection = (HttpsURLConnection) endpoint.toURL().openConnection(Proxy.NO_PROXY);
        connection.setSSLSocketFactory(bootstrap.getSocketFactory());
        // Preserve JSSE hostname verification when bootstrapping a self-signed local certificate.
        connection.setConnectTimeout(5000); connection.setReadTimeout(5000);
        try {
            connection.connect(); var certificates = connection.getServerCertificates();
            // A certificate-only JKS store can be read by JSSE without passing a password in JVM options.
            var store = KeyStore.getInstance("JKS"); store.load(null, password);
            for (int i = 0; i < certificates.length; i++) {
                ((X509Certificate) certificates[i]).checkValidity(); store.setCertificateEntry("cosmos-emulator-" + i, certificates[i]);
            }
            var parent = destination.toAbsolutePath().getParent(); if (parent != null) Files.createDirectories(parent);
            try (var output = Files.newOutputStream(destination, java.nio.file.StandardOpenOption.CREATE_NEW)) { store.store(output, password); }
        } finally { connection.disconnect(); }
    }
}
