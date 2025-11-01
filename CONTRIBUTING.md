# Contributing

As you find issues or suggestions for improvement, please reach out to the project maintainers and provide your feedback.

## Project Template Source

This project is based on the [ESM-Stack-dotNET-FullStack](https://github.com/ICSEng/ESM-Stack-dotNET-FullStack).

### Features

- **API Passthrough**
    - Opinionated pattern for calls that run on the next server:
        - `serverApiGetAsJson({ url, zodSchema, cacheDuration })` is called from server components.
        - Use standard `fetch` from client components (considering a `clientApiGetAsJson` method to follow the same pattern).
        - **Note:** Headers and auth values are accessed differently in server vs. client components; provided patterns clarify this for newcomers.
- **SSO Integration**
    - Uses the latest auth library.
    - API passthrough with SSO token implemented.
    - Mixed site (anonymous and secured) support: on secured pages, simply call `requireSignOn()` at the top of the server page component.
- **Impersonation Pattern**
    - Generalized spot to initiate impersonation; call your API to confirm impersonation and return data for the app to recognize the impersonated user.
    - Front end passes a header to the API indicating impersonation.
    - Utility URL for generating encryption keys for impersonation cookies, allowing unique keys per app.
- **Localization**
    - Language detection and switching fully supported.
    - Translated strings available to both API server and UI:
        - UI retrieves strings from the API (API handles English fallback).
        - Storybook supports translated strings and provides a UI element to change languages.
        - UI uses session storage to minimize requests and includes a provider for a simplified, resilient string structure.
        - Strings are lingoport-friendly and managed in conceptual groups.
- **Harness Feature Flags**
    - Feature flag convention built into the API, supporting one Harness instance for multiple apps.
        - Configure app key-prefix (e.g., `WISE_myFeature`).
        - Use `getFlagByConvention("myFeature")` for unique flags; `getFlagByConvention("myFeature", true)` for global flags.
        - `getFlagExact` method available to opt out of the convention pattern.
        - Supports JSON value feature flags.
    - UI uses the API for all feature flag requests, centralizing flag management.
    - React hooks request feature flags only once; Ctrl+` shortcut re-evaluates flags.
    - React component conditionally renders states: on, off, loading, and error.
- **Zod Validators**
    - Node server provides an easy pattern for Zod validation on API passthrough submissions.
    - Node implementation verifies data retrieved from the API against a Zod schema.

## Dev Containers, Docker, and the Developer Toolbox

This repository is intended to act as a developer toolbox: a collection of reproducible, local development resources (Docker services, devcontainer configuration, helper scripts, and example assets) that accelerate onboarding and provide common local dependencies for .NET and React development.

Key parts of the toolbox:

- `.devcontainer/devcontainer.json` — a VS Code DevContainer configuration that defines a ready-to-use development environment (base image, features like .NET, Node, Azure CLI, Docker-in-Docker, and recommended VS Code extensions). Use this to open the repo in a containerized VS Code session for consistent tooling across developers.

- `containers/docker-compose.yml` and `containers/docker-compose-common.yml` — example docker-compose files that can be used to bring up local services (database, mailhog, azurite, mocks, etc.). These are intentionally generic; teams should review and customize them to include only the services they need.

- `containers/.env` — environment variables used by the docker-compose files. Update this file (or use an env file of your own) when customizing services.

- Helper scripts at the repo root:
  - `docker_setup.ps1` — helper to bring up local containers and perform any post-setup steps.
  - `docker_down.ps1` — helper to tear down the local containers created by the setup script.
  - `setup_docker_container.ps1` and `post_devcontainer.ps1` — used by the DevContainer to finalize the container environment after creation.

How to use the toolbox (recommended quick paths):

1. Open in VS Code with DevContainers (recommended):
    - Install Docker Desktop and the VS Code Remote - Containers extension.
    - From VS Code: Command Palette -> Remote-Containers: Open Folder in Container... -> choose this repository. VS Code will use `.devcontainer/devcontainer.json` to build the environment.
    - The devcontainer runs `post_devcontainer.ps1` automatically (see `devcontainer.json` "postCreateCommand").

2. Use the Docker compose scripts locally (without a devcontainer):
    - Ensure Docker Desktop is running.
    - Run the helper script to create containers and example resources:

```powershell
.\docker_setup.ps1
```

    - When finished working, tear down the containers with:

```powershell
.\docker_down.ps1
```

Customizing for your project:

- Trim the services in `containers/docker-compose.yml` to only what you need. The project aims to provide a cafeteria of options but not to force every service on every project.
- Store sensitive or environment-specific values outside the repo (or in a private env file) and update `containers/.env` accordingly.
- If you add new services, update `docker_setup.ps1` and `docker_down.ps1` to include any needed lifecycle steps.

Notes and best practices:

- The DevContainer uses an official Microsoft devcontainer image and a set of features (Azure CLI, .NET workloads, Docker-in-Docker) to mirror typical developer needs. Review `.devcontainer/devcontainer.json` when you need additional tools.
- Running Docker Desktop with WSL2 backend on Windows typically provides the most consistent experience. If using Windows containers, adapt the compose files accordingly.
- Keep composition small and targeted for local development — avoid bringing up heavy or production-only services unless necessary.
- This repository contains example mappings and fixture files under `containers/__files/` used for mock responses during local development.

If you'd like, I can also:

- Add a small README under `containers/` summarizing the compose services provided and which env vars to change.
- Improve `docker_setup.ps1` to accept a `--profile` parameter so teams can bring up only a subset of services (database, cache, or mocks).


## Files and Project Structure

The following sections represent the folder structure from the root of the project. This is general instructions on where various project files should be stored and managed.

### /

Base repository and project configuration files are found here. This includes the gitignore, gitattributes, readme.md and other base files for the git repository. Additionally, other files such as the various PowerShell scripts are support files around updating the jFrog tokens and docker configuration and setup scripts.

Various project source files should not be stored at this root level and should instead be stored under the '/src' folder.

Lastly, the *.sln file should be found at the root level for convenience in starting your project in Visual Studio.

### /.devcontainer

Dev Container configuration file for setting up a Docker dev container instance. After forking or using this repo as a template you'll want to Update the "name" to reflect your new project.

### /.github

Various github related actions and configuration files. This also maintains the 'CODEOWNERS' file which puts more granular security controls on the repository. Lastly the 'copilot-instructions.md' file can be updated as needed to help provide additional context about your project to GitHub Copilot.

### /.sonarlint

This is where the sonar lint static code analysis configuration file is stored.

### /.vscode

VSCode stores configuration details that can be shared between all projects in this folder. The "extensions.json" file provides the Architect or Lead Developer the ability to specify recommended plugins for VSCode. Add / Remove those plugins that will provide maximum benefit to the team.

### /containers

This folder is somewhat devops related, but focuses on the setup and configuration for the dev container. The docker-compose.yml script is to be used to do post setup actions for the dev container and adding any additional resources needed for the project. For those who are not using the dev container, this should be used to configure your local environment by creating a docker collection of resources for your project. The PowerShell commands './docker_setup.ps1' and './docker_down.ps1' will setup or tear down the docker containers. When forking or using this project as a template you'll want to update the following.

./docker_setup.ps1
./docker_down.ps1
./containers/docker-compose.yml
./containers/docker-compose-common.yml

As this is example / boilerplate renaming resources, configurations, etc. to meet your project needs is recommended.

### /devops

This is specific for building and deploying the project solution artifacts. Build and deployment yaml scripts are to be stored here and then linked to Azure DevOps for build and deployment. If Terraform resources are needed they should be stored and updated here.
