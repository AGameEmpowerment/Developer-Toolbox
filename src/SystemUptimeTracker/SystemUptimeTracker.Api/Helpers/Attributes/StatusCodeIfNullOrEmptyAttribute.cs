using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace SystemUptimeTracker.Api.Helpers.Attributes;

public class StatusCodeIfNullOrEmptyAttribute : ActionFilterAttribute
{
    private readonly HttpStatusCode _statusCode;

    public StatusCodeIfNullOrEmptyAttribute(HttpStatusCode statusCode)
    {
        _statusCode = statusCode;
    }

    public override void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Result is ObjectResult objectResult)
        {
            object? value = objectResult.Value;
            if (ShouldReturnStatusCode(value))
            {
                context.Result = new StatusCodeResult((int)_statusCode);
            }
        }

        base.OnActionExecuted(context);
    }

    private static bool ShouldReturnStatusCode(object? value)
    {
        if (value == null)
        {
            return true;
        }

        if (value is string text)
        {
            return string.IsNullOrEmpty(text);
        }

        if (value is Array array)
        {
            return array.Length == 0;
        }

        if (value is ICollection collection)
        {
            return collection.Count == 0;
        }

        if (TryGetReadOnlyCollectionCount(value, out int readOnlyCollectionCount))
        {
            return readOnlyCollectionCount == 0;
        }

        if (value is IQueryable)
        {
            return false;
        }

        return value is IEnumerable enumerable && IsEnumerableEmpty(enumerable);
    }

    private static bool IsEnumerableEmpty(IEnumerable enumerable)
    {
        IEnumerator enumerator = enumerable.GetEnumerator();
        try
        {
            return !enumerator.MoveNext();
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }
    }

    private static bool TryGetReadOnlyCollectionCount(object value, out int count)
    {
        Type? readOnlyCollectionType = value.GetType()
            .GetInterfaces()
            .FirstOrDefault(interfaceType =>
                interfaceType.IsGenericType &&
                interfaceType.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>));

        if (readOnlyCollectionType?.GetProperty(nameof(IReadOnlyCollection<object>.Count))?.GetValue(value) is int collectionCount)
        {
            count = collectionCount;
            return true;
        }

        count = 0;
        return false;
    }
}