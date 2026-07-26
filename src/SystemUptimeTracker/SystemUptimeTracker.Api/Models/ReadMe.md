# Models

## Folder Structure

Keep a distinction between models presented to the UI and models that are loaded from data sources.

### Ui Models

Ui Models should be catered specifically to the precise needs of a UI component rather than being a direct representation of a data source. Group them conceptually as appropriate.

### Data Models

A recommended pattern is to use a folder for each data source and, when needed, a subfolder for each application-owned domain.
and keep models from each future application domain in their respective folders. Keep external service contracts separate from application-owned models when integrations are added.
