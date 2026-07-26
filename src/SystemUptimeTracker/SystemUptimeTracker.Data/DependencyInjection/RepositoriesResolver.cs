using Microsoft.Extensions.DependencyInjection;

namespace SystemUptimeTracker.Data.DependencyInjection;

public static class RepositoriesResolver
{
    public static void RegisterDependencies(IServiceCollection service)
    {
        // Note: The DB Context is registered in the application layer. Repositories are registered here.

        #region Database Repositories


        #endregion

        #region HttpClient Repositories


        #endregion
    }
}
