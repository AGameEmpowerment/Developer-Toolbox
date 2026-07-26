using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SystemUptimeTracker.Common.Helpers;
using SystemUptimeTracker.Common.Helpers.Data;
using SystemUptimeTracker.Common.Repositories.Interfaces;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;

namespace SystemUptimeTracker.Common.Repositories;

public class DbContextBase<TDbContext> : DbContext, IDbContextBase where TDbContext : DbContext
{
    private readonly string? _connectionString;
    private ILogger<TDbContext>? _logger;

    protected DbContextBase(DbContextOptions<TDbContext> options) : base(options)
    {
    }

    protected DbContextBase(DbContextOptionsBuilder<TDbContext> builder) : base(builder.Options)
    {
        OnConfiguring(builder);
    }

    protected DbContextBase(string connectionString, DbContextOptions<TDbContext> options) : base(options)
    {
        _connectionString = connectionString;
    }

    public DbContextBase(DbContextOptions options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        GuidFunctions.Register(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    protected sealed override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (!string.IsNullOrWhiteSpace(_connectionString))
        {
            optionsBuilder.UseSqlServer(_connectionString);
        }

        if (Debugger.IsAttached)
        {
            optionsBuilder.EnableSensitiveDataLogging();
            optionsBuilder.EnableDetailedErrors();
            optionsBuilder.UseLoggerFactory(LoggerSupport.GetLoggerFactory(null));
        }

        base.OnConfiguring(optionsBuilder);
    }

    #region Public Methods

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ChangeSummary changeSummary = SummarizeTrackedChanges();

        if (changeSummary.HasChanges)
        {
            Logger.LogInformation(
                "Saving changes for {DbContext}. Added={AddedCount} Modified={ModifiedCount} Deleted={DeletedCount}.",
                typeof(TDbContext).Name,
                changeSummary.AddedCount,
                changeSummary.ModifiedCount,
                changeSummary.DeletedCount);
        }
        else
        {
            Logger.LogDebug("SaveChangesAsync called for {DbContext} with no tracked changes.", typeof(TDbContext).Name);
        }

        try
        {
            Validate();

            int affectedRows = await base.SaveChangesAsync(cancellationToken);

            Logger.LogInformation(
                "Saved changes for {DbContext}. AffectedRows={AffectedRows} Added={AddedCount} Modified={ModifiedCount} Deleted={DeletedCount}.",
                typeof(TDbContext).Name,
                affectedRows,
                changeSummary.AddedCount,
                changeSummary.ModifiedCount,
                changeSummary.DeletedCount);

            return affectedRows;
        }
        catch (ValidationException ex)
        {
            Logger.LogWarning(
                ex,
                "Validation failed while saving changes for {DbContext}. Added={AddedCount} Modified={ModifiedCount} Deleted={DeletedCount}.",
                typeof(TDbContext).Name,
                changeSummary.AddedCount,
                changeSummary.ModifiedCount,
                changeSummary.DeletedCount);
            throw;
        }
        catch (DbUpdateException ex)
        {
            Logger.LogError(
                ex,
                "Database update failed while saving changes for {DbContext}. Added={AddedCount} Modified={ModifiedCount} Deleted={DeletedCount}.",
                typeof(TDbContext).Name,
                changeSummary.AddedCount,
                changeSummary.ModifiedCount,
                changeSummary.DeletedCount);
            throw;
        }
    }

    public override int SaveChanges()
    {
        ChangeSummary changeSummary = SummarizeTrackedChanges();

        if (changeSummary.HasChanges)
        {
            Logger.LogInformation(
                "Saving changes for {DbContext}. Added={AddedCount} Modified={ModifiedCount} Deleted={DeletedCount}.",
                typeof(TDbContext).Name,
                changeSummary.AddedCount,
                changeSummary.ModifiedCount,
                changeSummary.DeletedCount);
        }
        else
        {
            Logger.LogDebug("SaveChanges called for {DbContext} with no tracked changes.", typeof(TDbContext).Name);
        }

        try
        {
            Validate();

            int affectedRows = base.SaveChanges();

            Logger.LogInformation(
                "Saved changes for {DbContext}. AffectedRows={AffectedRows} Added={AddedCount} Modified={ModifiedCount} Deleted={DeletedCount}.",
                typeof(TDbContext).Name,
                affectedRows,
                changeSummary.AddedCount,
                changeSummary.ModifiedCount,
                changeSummary.DeletedCount);

            return affectedRows;
        }
        catch (ValidationException ex)
        {
            Logger.LogWarning(
                ex,
                "Validation failed while saving changes for {DbContext}. Added={AddedCount} Modified={ModifiedCount} Deleted={DeletedCount}.",
                typeof(TDbContext).Name,
                changeSummary.AddedCount,
                changeSummary.ModifiedCount,
                changeSummary.DeletedCount);
            throw;
        }
        catch (DbUpdateException ex)
        {
            Logger.LogError(
                ex,
                "Database update failed while saving changes for {DbContext}. Added={AddedCount} Modified={ModifiedCount} Deleted={DeletedCount}.",
                typeof(TDbContext).Name,
                changeSummary.AddedCount,
                changeSummary.ModifiedCount,
                changeSummary.DeletedCount);
            throw;
        }
    }

    /// <summary>
    ///     Reloads the specified entity.
    /// </summary>
    /// <typeparam name="TEntity">The type of the entity.</typeparam>
    /// <param name="entity">The entity.</param>
    public void Reload<TEntity>(TEntity entity) where TEntity : class
    {
        try
        {
            Entry(entity).Reload();
        }
        catch
        {
            // ignored
        }
    }

    /// <summary>
    ///     Clears the state.
    /// </summary>
    /// <typeparam name="TEntity">The type of the entity.</typeparam>
    /// <param name="entity">The entity.</param>
    public void ClearState<TEntity>(TEntity entity) where TEntity : class
    {
        try
        {
            Entry(entity).CurrentValues.SetValues(Entry(entity).OriginalValues);
        }
        catch
        {
            // ignored
        }

        try
        {
            Entry(entity).Reload();
        }
        catch
        {
            // ignored
        }

        try
        {
            Entry(entity).State = EntityState.Unchanged;
        }
        catch
        {
            // ignored
        }
    }

    /// <summary>
    ///     Sets the database context configuration automatic detect changes.
    /// </summary>
    /// <param name="setAutoDetect">if set to <c>true</c> [set automatic detect].</param>
    public void SetDbContextConfigurationAutoDetectChanges(bool setAutoDetect)
    {
        ChangeTracker.AutoDetectChangesEnabled = setAutoDetect;
    }

    protected static DbContextOptions<TDbContext> GetOptionsGeneric(string connectionString)
    {
        // ReSharper disable once InvokeAsExtensionMethod
        return new DbContextOptionsBuilder<TDbContext>().UseSqlServer(connectionString).Options;
    }

    #endregion

    #region Private Methods

    private void Validate()
    {
        IEnumerable<object> entities = from e in ChangeTracker.Entries()
            where e.State == EntityState.Added
                  || e.State == EntityState.Modified
            select e.Entity;

        foreach (object entity in entities)
        {
            ValidationContext validationContext = new ValidationContext(entity);
            Validator.ValidateObject(entity, validationContext);
        }
    }

    private ILogger<TDbContext> Logger => _logger ??= ResolveLogger();

    private ILogger<TDbContext> ResolveLogger()
    {
        try
        {
            return this.GetService<ILoggerFactory>().CreateLogger<TDbContext>();
        }
        catch
        {
            return NullLogger<TDbContext>.Instance;
        }
    }

    private ChangeSummary SummarizeTrackedChanges()
    {
        int addedCount = 0;
        int modifiedCount = 0;
        int deletedCount = 0;

        foreach (EntityEntry entry in ChangeTracker.Entries())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    addedCount++;
                    break;
                case EntityState.Modified:
                    modifiedCount++;
                    break;
                case EntityState.Deleted:
                    deletedCount++;
                    break;
            }
        }

        return new ChangeSummary(addedCount, modifiedCount, deletedCount);
    }

    private readonly record struct ChangeSummary(int AddedCount, int ModifiedCount, int DeletedCount)
    {
        public bool HasChanges => AddedCount > 0 || ModifiedCount > 0 || DeletedCount > 0;
    }

    #endregion
}
