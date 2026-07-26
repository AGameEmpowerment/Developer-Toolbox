using SystemUptimeTracker.Common.Models;
using System.Linq.Expressions;

namespace SystemUptimeTracker.Common.Repositories.Interfaces;

/// <summary>
/// 
/// </summary>
/// <typeparam name="TEntityType"></typeparam>
/// <typeparam name="TKeyType"></typeparam>
public interface ILookupRepositoryTemplate<TEntityType, in TKeyType>
{
    /// <summary>
    /// This will return records based on the default or first primary key Id
    /// </summary>
    /// <param name="key1">The key.</param>
    /// <returns>The <typeparamref name="TEntityType" /> Record.</returns>
    TEntityType GetEntityById(TKeyType key1);

    /// <summary>
    /// This will return records based on the default or first primary key Id
    /// </summary>
    /// <param name="key1">The key.</param>
    /// <returns>The <typeparamref name="TEntityType" /> Record.</returns>
    Task<TEntityType> GetEntityByIdAsync(TKeyType key1);

    /// <summary>
    ///     Gets all records.
    /// </summary>
    /// <returns>A collection of all <typeparamref name="TEntityType" /> records.</returns>
    IList<TEntityType> GetAll();


    /// <summary>
    ///     Gets all records async
    /// </summary>
    /// <returns>A collection of all <typeparamref name="TEntityType" /> records.</returns>
    Task<IList<TEntityType>> GetAllAsync();

    /// <summary>
    /// Allows for paging by page and page size
    /// </summary>
    /// <param name="pageNumber"></param>
    /// <param name="pageSize"></param>
    /// <returns></returns>
    PagedObjectData<TEntityType> GetByPaging(int pageNumber, int pageSize);

    /// <summary>
    /// Allows for paging by page and page size
    /// </summary>
    /// <param name="pageNumber"></param>
    /// <param name="pageSize"></param>
    /// <returns></returns>
    Task<PagedObjectData<TEntityType>> GetByPagingAsync(int pageNumber, int pageSize);

    /// <summary>
    ///     Gets a queryable collection of records based on the
    ///     <param name="filter" />
    ///     provided.
    /// </summary>
    /// <returns>A filtered collection of <typeparamref name="TEntityType" /> records.</returns>
    IQueryable<TEntityType> Query(Expression<Func<TEntityType, bool>> filter);

    /// <summary>
    ///     Returns a query object.
    /// </summary>
    /// <returns>IQueryable<TEntityType /></returns>
    IQueryable<TEntityType> GetAsQueryable();
}