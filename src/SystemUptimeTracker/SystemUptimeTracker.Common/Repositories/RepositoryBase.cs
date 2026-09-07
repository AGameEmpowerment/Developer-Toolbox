using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SystemUptimeTracker.Common.Repositories.Interfaces;

namespace SystemUptimeTracker.Common.Repositories;

/// <summary>
///     Base abstract class used as a foundation for all of the other repository classes
/// </summary>
public abstract class RepositoryBase<TDbContext>
{
    // ReSharper disable once InconsistentNaming
    protected readonly TDbContext _context;
    private ILogger? _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RepositoryBase{TDbContext}" /> class.
    /// </summary>
    /// <param name="dbContext">The database context.</param>
    /// <exception cref="ArgumentNullException">dbContext</exception>
    /// <exception cref="InvalidCastException">Could not cast dbContext to {typeof(TDbContext)}</exception>
    protected RepositoryBase(IDbContextBase dbContext)
    {
        // ReSharper disable once JoinNullCheckWithUsage
        if (dbContext == null)
        {
            throw new ArgumentNullException(nameof(dbContext));
        }

        _context = (TDbContext)dbContext;

        if (_context == null)
        {
            throw new InvalidCastException($"Could not cast dbContext to {typeof(TDbContext)}");
        }
    }

    protected ILogger Logger => _logger ??= ResolveLogger();

    protected IDisposable? BeginRepositoryScope(string operationName)
    {
        return Logger.BeginScope(new Dictionary<string, object?>
        {
            ["module"] = GetType().Name,
            ["operation"] = operationName,
            ["dbContext"] = typeof(TDbContext).Name
        });
    }

    private ILogger ResolveLogger()
    {
        try
        {
            if (_context is DbContext dbContext)
            {
                return dbContext.GetService<ILoggerFactory>()
                    .CreateLogger(GetType());
            }
        }
        catch (InvalidOperationException)
        {
            // A context created without an internal service provider has no logger factory.
        }

        return NullLoggerFactory.Instance.CreateLogger(GetType());
    }
}
