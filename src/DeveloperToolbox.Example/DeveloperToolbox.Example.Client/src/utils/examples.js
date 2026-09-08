import "server-only";

const examples = Object.freeze([
  Object.freeze({
    id: "local-data",
    name: "Local data",
    summary: "Keep deterministic sample data close to the code that uses it.",
    topics: Object.freeze([
      "Server Components",
      "Immutable data",
      "Unit tests",
    ]),
  }),
  Object.freeze({
    id: "dynamic-routes",
    name: "Dynamic routes",
    summary: "Generate detail pages from stable string route parameters.",
    topics: Object.freeze(["App Router", "Metadata", "Static parameters"]),
  }),
  Object.freeze({
    id: "localization",
    name: "Localization",
    summary: "Select a supported dictionary from request language preferences.",
    topics: Object.freeze([
      "Language negotiation",
      "JSON dictionaries",
      "Fallbacks",
    ]),
  }),
]);

export function getExamples() {
  return examples;
}

export function getExampleById(id) {
  return examples.find((example) => example.id === id);
}
