using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SystemUptimeTracker.Api.Constants;
using SystemUptimeTracker.Api.Extensions;
using SystemUptimeTracker.Data.Identity;

namespace SystemUptimeTracker.Api.Services.Identity;

public sealed class IdentityRoleSeeder : IIdentityRoleSeeder
{
    private const string LEGACY_READ_ONLY_ROLE_NAME = "ReadOnly";
    private const string IDENTITY_MIGRATION_HISTORY_TABLE_NAME = "__EFMigrationsHistory";
    private const string INITIAL_IDENTITY_MIGRATION_NAME = "20260410145024_CreateIdentitySchema";
    private static readonly string[] IdentityTableNames =
    [
        "AspNetRoles",
        "AspNetUsers"
    ];

    private readonly ApplicationDbContext _dbContext;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<IdentityRoleSeeder> _logger;
    private readonly RoleManager<IdentityRole> _roleManager;

    public IdentityRoleSeeder(
        ApplicationDbContext dbContext,
        RoleManager<IdentityRole> roleManager,
        ILogger<IdentityRoleSeeder> logger,
        IHostEnvironment hostEnvironment)
    {
        _dbContext = dbContext;
        _hostEnvironment = hostEnvironment;
        _roleManager = roleManager;
        _logger = logger;
    }

    public async Task EnsureSeedDataAsync(CancellationToken cancellationToken = default)
    {
        if (_dbContext.Database.IsRelational())
        {
            if (StartupMigrationPolicy.CanApply(_hostEnvironment))
            {
                await MigrateIdentitySchemaAsync(cancellationToken);
            }
            else
            {
                await ValidateRelationalIdentitySchemaReadinessAsync(cancellationToken);
            }
        }
        else
        {
            await _dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }

        await NormalizeLegacyReadRoleAsync(cancellationToken);

        foreach (string roleName in ApplicationRoleNames.All)
        {
            if (await _roleManager.RoleExistsAsync(roleName))
            {
                continue;
            }

            IdentityResult createResult = await _roleManager.CreateAsync(new IdentityRole(roleName));
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException($"Failed to seed role '{roleName}'. {FormatErrors(createResult)}");
            }

            _logger.LogInformation("Seeded identity role {RoleName}.", roleName);
        }
    }

    private async Task MigrateIdentitySchemaAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.Database.MigrateAsync(cancellationToken);
        }
        catch (SqlException exception)
        {
            if (exception.Number != 2714
                || !await HasExistingIdentitySchemaWithoutMigrationHistoryAsync(cancellationToken))
            {
                throw;
            }

            return;
        }
    }

    private async Task ValidateRelationalIdentitySchemaReadinessAsync(CancellationToken cancellationToken)
    {
        if (!await _dbContext.Database.CanConnectAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "Unable to connect to the configured identity database. " +
                "In non-development environments, startup migrations are disabled by default. " +
                "Deploy with an existing database and apply migrations before startup, or set SystemUptimeTracker__ApplyStartupMigrations=true when the SQL principal has migration permissions.");
        }

        IEnumerable<string> pendingMigrations = await _dbContext.Database.GetPendingMigrationsAsync(cancellationToken);
        if (pendingMigrations.Any())
        {
            throw new InvalidOperationException(
                $"The identity database has pending migrations ({string.Join(", ", pendingMigrations)}). " +
                "Apply migrations as part of deployment, or set SystemUptimeTracker__ApplyStartupMigrations=true when the SQL principal has migration permissions.");
        }
    }

    private async Task<bool> HasExistingIdentitySchemaWithoutMigrationHistoryAsync(CancellationToken cancellationToken)
    {
        DbConnection connection = _dbContext.Database.GetDbConnection();
        bool shouldCloseConnection = connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            int existingIdentityTableCount = await ExecuteScalarAsync(
                connection,
                cancellationToken,
                $"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME IN ('{string.Join("','", IdentityTableNames)}')");

            if (existingIdentityTableCount < IdentityTableNames.Length)
            {
                return false;
            }

            int matchingMigrationHistoryCount = await ExecuteScalarAsync(
                connection,
                cancellationToken,
                $"SELECT COUNT(*) FROM [{IDENTITY_MIGRATION_HISTORY_TABLE_NAME}] WHERE [MigrationId] = '{INITIAL_IDENTITY_MIGRATION_NAME}'");

            return matchingMigrationHistoryCount == 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<int> ExecuteScalarAsync(
        DbConnection connection,
        CancellationToken cancellationToken,
        string commandText)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = commandText;

        object? scalar = await command.ExecuteScalarAsync(cancellationToken);
        return scalar switch
        {
            int value => value,
            long value => checked((int)value),
            short value => value,
            byte value => value,
            decimal value => decimal.ToInt32(value),
            _ => Convert.ToInt32(scalar, CultureInfo.InvariantCulture)
        };
    }

    private async Task NormalizeLegacyReadRoleAsync(CancellationToken cancellationToken)
    {
        IdentityRole? legacyReadOnlyRole = await _roleManager.FindByNameAsync(LEGACY_READ_ONLY_ROLE_NAME);
        if (legacyReadOnlyRole is null)
        {
            return;
        }

        IdentityRole? readRole = await _roleManager.FindByNameAsync(ApplicationRoleNames.READ);
        if (readRole is null)
        {
            await _roleManager.SetRoleNameAsync(legacyReadOnlyRole, ApplicationRoleNames.READ);
            IdentityResult renameResult = await _roleManager.UpdateAsync(legacyReadOnlyRole);
            if (!renameResult.Succeeded)
            {
                throw new InvalidOperationException($"Failed to rename legacy role '{LEGACY_READ_ONLY_ROLE_NAME}' to '{ApplicationRoleNames.READ}'. {FormatErrors(renameResult)}");
            }

            _logger.LogInformation(
                "Renamed legacy identity role {LegacyRoleName} to {RoleName}.",
                LEGACY_READ_ONLY_ROLE_NAME,
                ApplicationRoleNames.READ);
            return;
        }

        await MergeLegacyReadRoleAssignmentsAsync(
            legacyReadOnlyRole.Id,
            readRole.Id,
            cancellationToken);

        IdentityResult deleteResult = await _roleManager.DeleteAsync(legacyReadOnlyRole);
        if (!deleteResult.Succeeded)
        {
            throw new InvalidOperationException($"Failed to delete legacy role '{LEGACY_READ_ONLY_ROLE_NAME}'. {FormatErrors(deleteResult)}");
        }

        _logger.LogInformation(
            "Merged legacy identity role {LegacyRoleName} into {RoleName}.",
            LEGACY_READ_ONLY_ROLE_NAME,
            ApplicationRoleNames.READ);
    }

    private async Task MergeLegacyReadRoleAssignmentsAsync(
        string legacyRoleId,
        string readRoleId,
        CancellationToken cancellationToken)
    {
        List<IdentityUserRole<string>> legacyAssignments = await _dbContext.UserRoles
            .Where(userRole => userRole.RoleId == legacyRoleId)
            .ToListAsync(cancellationToken);

        if (legacyAssignments.Count == 0)
        {
            return;
        }

        HashSet<string> readUserIds = (await _dbContext.UserRoles
                .Where(userRole => userRole.RoleId == readRoleId)
                .Select(userRole => userRole.UserId)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (IdentityUserRole<string> legacyAssignment in legacyAssignments)
        {
            if (readUserIds.Contains(legacyAssignment.UserId))
            {
                _dbContext.UserRoles.Remove(legacyAssignment);
                continue;
            }

            _dbContext.UserRoles.Remove(legacyAssignment);
            _dbContext.UserRoles.Add(new IdentityUserRole<string>
            {
                UserId = legacyAssignment.UserId,
                RoleId = readRoleId
            });
            readUserIds.Add(legacyAssignment.UserId);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string FormatErrors(IdentityResult result)
    {
        return string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}"));
    }
}
