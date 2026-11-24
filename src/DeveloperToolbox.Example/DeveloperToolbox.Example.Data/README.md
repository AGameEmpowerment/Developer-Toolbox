---
post_title: EF Core transactional database examples
author1: Edu Developer Toolbox maintainers
post_slug: ef-core-database-examples
microsoft_alias: n/a
featured_image: n/a
categories: [documentation]
tags: [dotnet, ef-core, local-development]
ai_note: AI assisted
summary: Local EF Core SQL Server and PostgreSQL database samples.
post_date: 2026-10-12
---

This library demonstrates **local development only** database creation, inserts,
queries, and message deduplication with EF Core 10. It has separate SQL Server
and PostgreSQL contexts using the same `Messages` schema. The console app,
Azure Functions, and Lambda samples reuse `DatabaseSamples`.

Supply connections through configuration rather than embedding credentials:

```text
ConnectionStrings__SqlServer=Server=127.0.0.1,10433;Database=ToolboxSamples;User Id=sa;Password=<containers/.env MSSQL_SA_PASSWORD>;Encrypt=True;TrustServerCertificate=True
ConnectionStrings__Postgres=Host=localhost;Port=10432;Database=ToolboxSamples;Username=<containers/.env POSTGRES_USER>;Password=<containers/.env POSTGRES_PASSWORD>
```

The local database administrator needs permission to create `ToolboxSamples`.
The facade rejects other database names and remote hosts. The SQL Server sample
also rejects failover partners, which could redirect a connection away from the
validated server. SQL Server's local
certificate is explicitly trusted in the example connection; use proper trusted
certificates outside this disposable toolbox.

```csharp
services.AddToolboxDatabases(configuration);

// Inserts and reads the most recent ten messages inside an explicit transaction.
var sqlResult = await databases.SqlServerAsync("Hello SQL Server", cancellationToken);
var postgresResult = await databases.PostgresAsync("Hello PostgreSQL", cancellationToken);

// Use the same ID on redelivery. Only the first successful insert returns true.
var inserted = await databases.SaveSqlServerMessageAsync(
    messageId, text, "servicebus-queue", cancellationToken);
```

The primary key is the stable message ID. Fields are limited to 128 ID, 4096
text, and 64 source characters, with a UTC creation time and timestamp index.
EF Core supplies parameterized database access; queries are bounded and use
`AsNoTracking`. An existence check handles normal repeated deliveries, and the
database primary key also handles concurrent deliveries. Only provider duplicate
key errors are treated as an already-persisted message. Other errors propagate
so queue consumers retry rather than acknowledge a failed write.

`EnsureCreatedAsync` creates an absent database and its initial schema. It does
not maintain an existing schema. These disposable examples deliberately omit
production migrations. To reset a changed schema, recreate only your sample
`ToolboxSamples` database; retain all other toolbox databases.

Run the deterministic validation tests from the repository root:

```powershell
dotnet test tests/DeveloperToolbox.Example.Data.Tests
```

Real-provider concurrent redelivery tests are opt-in. Set
`TOOLBOX_RUN_DATABASE_INTEGRATION=1` and both connection environment variables
above, then run the same command. They assert one insert across concurrent
deliveries and delete only the rows created by each test.

References: [EF Core create APIs](https://learn.microsoft.com/ef/core/managing-schemas/ensure-created),
[EF Core transactions](https://learn.microsoft.com/ef/core/saving/transactions).
