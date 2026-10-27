using System.Collections;
using System.Text;
using System.Text.RegularExpressions;

namespace DeveloperToolbox.Example.App;

/// <summary>Reads selected local toolbox settings as data without executing shell syntax.</summary>
internal static class ToolboxEnvironment
{
    private static readonly Regex Key = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);
    private static readonly Regex Reference = new(@"\$(\$|\{([^{}]+)\}|([A-Za-z_][A-Za-z0-9_]*))", RegexOptions.CultureInvariant);
    private static readonly Regex Expression = new(@"^([A-Za-z_][A-Za-z0-9_]*)(?:(:-|-|:\+|\+|:\?|\?)(.*))?$", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> ExactSettings = new(StringComparer.Ordinal)
    {
        "ServiceBusConnection", "RABBITMQ_DEFAULT_USER", "RABBITMQ_DEFAULT_PASS", "RABBITMQ_HOST", "RABBITMQ_PORT",
        "TOOLBOX_AWS_ENDPOINT", "ToolboxAzureStorage", "AzureWebJobsStorage"
    };

    internal static IReadOnlyDictionary<string, string?> Read(string path)
    {
        var environment = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!, StringComparer.Ordinal);
        var assignments = new Dictionary<string, Assignment>(StringComparer.Ordinal);
        var defaults = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, ".env.example");
        if (File.Exists(defaults))
        {
            Parse(File.ReadAllText(defaults), assignments);
        }
        Parse(File.ReadAllText(path), assignments);
        return Resolve(assignments, environment);
    }

    internal static IReadOnlyDictionary<string, string?> Parse(string text, IReadOnlyDictionary<string, string> environment)
    {
        var assignments = new Dictionary<string, Assignment>(StringComparer.Ordinal);
        Parse(text, assignments);
        return Resolve(assignments, environment);
    }

    private static void Parse(string text, Dictionary<string, Assignment> assignments)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }
            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line[7..].TrimStart();
            }
            var separator = line.IndexOf('=');
            if (separator < 1 || !Key.IsMatch(line[..separator].Trim()) || line.Contains('\0'))
            {
                throw new FormatException("Invalid dotenv assignment. Check the local toolbox settings file.");
            }
            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            var interpolate = true;
            if (value.StartsWith('\'') || value.StartsWith('"'))
            {
                var quote = value[0];
                interpolate = quote == '"';
                var parsed = new StringBuilder();
                var closed = false;
                for (var index = 1; index < value.Length; index++)
                {
                    var character = value[index];
                    if (character == quote)
                    {
                        var tail = value[(index + 1)..].Trim();
                        if (tail.Length > 0 && !tail.StartsWith('#'))
                        {
                            throw new FormatException("Unexpected text after a quoted dotenv value.");
                        }
                        closed = true;
                        break;
                    }
                    if (character == '\\' && index + 1 < value.Length)
                    {
                        var following = value[index + 1];
                        if (following == quote || (interpolate && "nrt\\\"$".Contains(following)))
                        {
                            parsed.Append(interpolate ? following switch
                            {
                                'n' => '\n', 'r' => '\r', 't' => '\t', '$' => '\0', _ => following
                            } : following);
                            index++;
                            continue;
                        }
                    }
                    parsed.Append(character);
                }
                if (!closed)
                {
                    throw new FormatException("Unterminated quoted dotenv value; use one-line assignments.");
                }
                value = parsed.ToString();
            }
            else
            {
                value = Regex.Split(value, @"\s+#", RegexOptions.CultureInvariant)[0].TrimEnd();
            }
            assignments[name] = new Assignment(value, interpolate);
        }
    }

    private static IReadOnlyDictionary<string, string?> Resolve(Dictionary<string, Assignment> assignments,
        IReadOnlyDictionary<string, string> environment)
    {
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        string ResolveValue(string name, HashSet<string> active)
        {
            if (name == "LOCALSTACK_AUTH_TOKEN")
            {
                throw new FormatException("Licensing tokens cannot be copied into local example settings.");
            }
            if (environment.TryGetValue(name, out var literal)) { return literal; }
            if (resolved.TryGetValue(name, out var cached)) { return cached; }
            if (!assignments.TryGetValue(name, out var assignment)) { return ""; }
            if (!active.Add(name)) { throw new FormatException("Cyclic dotenv reference in local example settings."); }
            try
            {
                var value = assignment.Value;
                if (assignment.Interpolate)
                {
                    if (Reference.Replace(value, "").Contains("${", StringComparison.Ordinal))
                    {
                        throw new FormatException("Unsupported nested dotenv interpolation.");
                    }
                    value = Reference.Replace(value, match =>
                    {
                        if (match.Groups[1].Value == "$") { return "\0"; }
                        var expression = Expression.Match(match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value);
                        if (!expression.Success) { throw new FormatException("Unsupported dotenv interpolation."); }
                        var reference = expression.Groups[1].Value;
                        var operation = expression.Groups[2].Value;
                        var fallback = expression.Groups[3].Value;
                        var current = ResolveValue(reference, active);
                        var present = (environment.ContainsKey(reference) || assignments.ContainsKey(reference)) &&
                            (!operation.StartsWith(':') || current.Length > 0);
                        current = operation switch
                        {
                            "-" or ":-" => present ? current : fallback,
                            "+" or ":+" => present ? fallback : "",
                            "?" or ":?" when !present => throw new FormatException("A required dotenv reference is missing."),
                            _ => current
                        };
                        // Quote a whole ADO.NET component so a password's semicolon cannot add connection options.
                        if (name.StartsWith("ConnectionStrings__", StringComparison.Ordinal) &&
                            Regex.IsMatch(assignment.Value[..match.Index], @"(?:^|;)\s*[^=;]+\s*=\s*$") &&
                            Regex.IsMatch(assignment.Value[(match.Index + match.Length)..], @"^\s*(?:;|$)"))
                        {
                            current = '"' + current.Replace("\"", "\"\"") + '"';
                        }
                        return current;
                    });
                }
                resolved[name] = value.Replace('\0', '$');
                return resolved[name];
            }
            finally { active.Remove(name); }
        }

        return assignments.Keys.Where(name => name.StartsWith("ConnectionStrings__", StringComparison.Ordinal) ||
                name.StartsWith("Resources__", StringComparison.Ordinal) || ExactSettings.Contains(name))
            .ToDictionary(name => name.Replace("__", ":", StringComparison.Ordinal),
                name => (string?)ResolveValue(name, new HashSet<string>(StringComparer.Ordinal)), StringComparer.OrdinalIgnoreCase);
    }

    private sealed record Assignment(string Value, bool Interpolate);
}
