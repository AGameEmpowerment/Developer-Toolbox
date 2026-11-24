package developertoolbox.core;

public final class Main {
    private Main() { }
    public static void main(String[] args) {
        if (args.length == 2 && args[0].equals("trust-cosmos")) {
            try {
                var settings = Settings.fromEnvironment();
                var password = settings.require("JAVA_TRUSTSTORE_PASSWORD").toCharArray();
                try { CosmosCertificate.write(settings, java.nio.file.Path.of(args[1]), password); }
                finally { java.util.Arrays.fill(password, '\0'); }
                System.out.println("Created a dedicated local Cosmos trust store; configure JAVA_TOOL_OPTIONS before running Cosmos.");
            } catch (Exception ex) { System.err.println("Certificate import failed: " + ex.getClass().getSimpleName() + "."); System.exit(1); }
            return;
        }
        if (args.length != 1 || !SdkSamples.RESOURCES.contains(args[0].toLowerCase(java.util.Locale.ROOT))) {
            System.err.println("Usage: java -jar core-1.0-SNAPSHOT-cli.jar <" + String.join("|", new java.util.TreeSet<>(SdkSamples.RESOURCES)) + ">");
            System.exit(2); return;
        }
        try (var samples = new SdkSamples(Settings.fromEnvironment())) { System.out.println(samples.run(args[0])); }
        catch (Exception ex) { System.err.println("Java sample failed: " + ex.getClass().getSimpleName() + ". Check local resource configuration."); System.exit(1); }
    }
}
