# WireMock Mappings

This folder contains the WireMock mappings used for API virtualization and testing. Each mapping file defines a specific API endpoint and its behavior.

## Structure

- `__files`: Contains any files that are referenced in the mappings (e.g., request bodies, response bodies).
- `mappings`: Contains the mapping files themselves, typically named after the API endpoint they represent.
- `extensions`: Contains any custom extensions or plugins for WireMock.

Ensure that the folder structure is maintained as WireMock relies on this organization to locate the mappings and associated files correctly.