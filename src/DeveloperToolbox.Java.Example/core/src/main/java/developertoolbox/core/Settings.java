package developertoolbox.core;

import java.io.IOException;
import java.net.URI;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.Set;
import java.util.regex.Pattern;

/** Environment configuration for local examples, with an optional data-only dotenv file. */
public final class Settings {
    private final Map<String, String> values;
    public Settings(Map<String, String> values) { this.values = Map.copyOf(values); }
    public static Settings fromEnvironment() {
        var values = new LinkedHashMap<String, String>();
        var file = System.getenv("TOOLBOX_ENV_FILE");
        if (file != null && !file.isBlank()) {
            try { values.putAll(readDotEnv(Path.of(file))); }
            catch (IOException ex) { throw new IllegalArgumentException("Cannot read TOOLBOX_ENV_FILE.", ex); }
        }
        values.putAll(System.getenv());
        return new Settings(values);
    }
    public String get(String key, String fallback) {
        var value = values.get(key);
        return value == null || value.isBlank() ? fallback : value;
    }
    public String require(String key) {
        var value = get(key, "");
        if (value.isBlank() || value.equals("auto")) throw new IllegalArgumentException("Configure " + key + " for local development.");
        return value;
    }
    public int port(String key, int fallback) {
        int value = Integer.parseInt(get(key, Integer.toString(fallback)));
        if (value < 1 || value > 65535) throw new IllegalArgumentException("Invalid local port.");
        return value;
    }
    public URI endpoint(String key, String fallback, String... composeHosts) {
        var uri = URI.create(get(key, fallback));
        if (!Set.of("http", "https").contains(uri.getScheme()) || uri.getUserInfo() != null
            || uri.getFragment() != null || uri.getQuery() != null) throw new IllegalArgumentException("A local HTTP endpoint is required.");
        localHost(uri.getHost(), composeHosts);
        return uri;
    }
    public static void localHost(String host, String... composeHosts) {
        if (host == null) throw new IllegalArgumentException("A local host is required.");
        var allowed = new java.util.HashSet<>(Set.of("localhost", "127.0.0.1", "::1", "[::1]"));
        allowed.addAll(java.util.List.of(composeHosts));
        if (!allowed.contains(host.toLowerCase(java.util.Locale.ROOT))) throw new IllegalArgumentException("Examples accept only loopback or toolbox Compose hosts.");
    }
    static Map<String, String> readDotEnv(Path file) throws IOException {
        var result = new LinkedHashMap<String, String>();
        var reference = Pattern.compile("\\$(\\$|\\{([A-Za-z_][A-Za-z0-9_]*)}|([A-Za-z_][A-Za-z0-9_]*))");
        for (var line : Files.readAllLines(file)) {
            if (line.startsWith("\uFEFF")) line = line.substring(1);
            line = line.strip();
            if (line.isEmpty() || line.startsWith("#")) continue;
            if (line.indexOf('\0') >= 0) throw new IllegalArgumentException("NUL is not allowed in dotenv assignments.");
            if (line.startsWith("export ")) line = line.substring(7).stripLeading();
            int equals = line.indexOf('=');
            if (equals < 1) throw new IllegalArgumentException("Invalid dotenv assignment.");
            var key = line.substring(0, equals).strip();
            if (!key.matches("[A-Za-z_][A-Za-z0-9_]*")) throw new IllegalArgumentException("Invalid dotenv key.");
            var parsed = dotenvValue(line.substring(equals + 1).strip());
            var value = parsed.value();
            if (parsed.interpolate()) value = reference.matcher(value).replaceAll(match -> {
                if (match.group(1).equals("$")) return "\0";
                var name = match.group(2) == null ? match.group(3) : match.group(2);
                // Environment and previously resolved values are literal, never another shell program.
                return java.util.regex.Matcher.quoteReplacement(System.getenv().getOrDefault(name, result.getOrDefault(name, "")));
            });
            result.put(key, value.replace('\0', '$'));
        }
        return result;
    }

    /** Parses one-line Compose quoting while keeping diagnostics independent of credential values. */
    private static DotenvValue dotenvValue(String value) {
        if (!value.startsWith("'") && !value.startsWith("\""))
            return new DotenvValue(value.split("\\s+#", 2)[0].stripTrailing(), true);
        char quote = value.charAt(0);
        boolean interpolate = quote == '"';
        var parsed = new StringBuilder();
        for (int index = 1; index < value.length(); index++) {
            char character = value.charAt(index);
            if (character == quote) {
                var tail = value.substring(index + 1).strip();
                if (!tail.isEmpty() && !tail.startsWith("#")) throw new IllegalArgumentException("Unexpected text after quoted dotenv value.");
                return new DotenvValue(parsed.toString(), interpolate);
            }
            if (character == '\\' && index + 1 < value.length()) {
                char following = value.charAt(index + 1);
                if (following == quote || interpolate && "nrt\\\"$".indexOf(following) >= 0) {
                    parsed.append(interpolate ? switch (following) {
                        case 'n' -> '\n'; case 'r' -> '\r'; case 't' -> '\t'; case '$' -> '\0'; default -> following;
                    } : following);
                    index++;
                    continue;
                }
            }
            parsed.append(character);
        }
        throw new IllegalArgumentException("Unterminated quoted dotenv value; use one-line assignments.");
    }

    private record DotenvValue(String value, boolean interpolate) { }
}
