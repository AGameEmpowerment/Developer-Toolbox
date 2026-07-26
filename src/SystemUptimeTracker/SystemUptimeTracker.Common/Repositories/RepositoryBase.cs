using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SystemUptimeTracker.Common.Repositories.Interfaces;

namespace SystemUptimeTracker.Common.Repositories;

/// <summary>
///     Base abstract class used as a foundation for all of the other repository classes
/// </summary>
public abstract class RepositoryBase<TDbContext> : IDisposable
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

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    // NOTE: Leave out the finalizer altogether if this class doesn't
    // own unmanaged resources itself, but leave the other methods
    // exactly as they are.
    ~RepositoryBase()
    {
        // Finalizer calls Dispose(false)
        Dispose(false);
    }

    // The bulk of the clean-up code is implemented in Dispose(bool)
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            //// free managed resources
            //if( managedResource != null )
            //{
            //	managedResource.Dispose();
            //	managedResource = null;
            //}
        }

        // free native resources if there are any.
        //if( nativeResource != IntPtr.Zero )
        //{
        //	Marshal.FreeHGlobal( nativeResource );
        //	nativeResource = IntPtr.Zero;
        //}
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
        catch
        {
        }

        return NullLoggerFactory.Instance.CreateLogger(GetType());
    }
}