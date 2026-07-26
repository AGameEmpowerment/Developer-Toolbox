using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SystemUptimeTracker.Api.Constants;
using SystemUptimeTracker.Api.Helpers.Web;
using SystemUptimeTracker.Api.Models.Identity;
using SystemUptimeTracker.Data.Identity;
namespace SystemUptimeTracker.Api.Extensions;

public static class ProtectedApplicationEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapSystemUptimeTrackerProtectedApplicationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder usersGroup = endpoints.MapGroup("/api/users");
        usersGroup.MapGet(string.Empty, GetUserManagementSummaries).RequireUserManagementPolicy();
        usersGroup.MapPut("/{userId}/roles", UpdateUserRoles).RequireUserManagementPolicy();
        usersGroup.MapPut("/{userId}/activation", UpdateUserActivation).RequireUserManagementPolicy();

        return endpoints;
    }

    private static async Task<IResult> GetUserManagementSummaries(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        ApplicationDbContext? dbContext = serviceProvider.GetService<ApplicationDbContext>();
        if (dbContext is null)
        {
            return Results.Ok(Array.Empty<UserManagementSurfaceSummary>());
        }

        List<ApplicationUser> users = await dbContext.Users
            .OrderBy(user => user.Email)
            .ToListAsync(cancellationToken);

        var roleAssignments = await dbContext.UserRoles
            .Join(
                dbContext.Roles,
                userRole => userRole.RoleId,
                role => role.Id,
                (userRole, role) => new
                {
                    userRole.UserId,
                    RoleName = role.Name ?? string.Empty
                })
            .ToListAsync(cancellationToken);

        ILookup<string, string> rolesByUserId = roleAssignments
            .Where(roleAssignment => !string.IsNullOrWhiteSpace(roleAssignment.RoleName))
            .ToLookup(
                roleAssignment => roleAssignment.UserId,
                roleAssignment => roleAssignment.RoleName);

        UserManagementSurfaceSummary[] summaries = users
            .Select(user => new UserManagementSurfaceSummary(
                user.Id,
                user.Email ?? string.Empty,
                user.DisplayName,
                rolesByUserId[user.Id].OrderBy(role => role, StringComparer.OrdinalIgnoreCase).ToArray(),
                user.IsActive,
                user.CreatedAtUtc,
                user.LastLoginAtUtc))
            .ToArray();

        return Results.Ok(summaries);
    }

    private static async Task<IResult> UpdateUserRoles(
        string userId,
        UpdateUserRolesRequest? request,
        IServiceProvider serviceProvider)
    {
        if (request?.Roles is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(UpdateUserRolesRequest.Roles)] = ["At least one roles collection is required. Use an empty array to remove all roles."]
            });
        }

        UserManager<ApplicationUser>? userManager = serviceProvider.GetService<UserManager<ApplicationUser>>();
        if (userManager is null)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        ApplicationUser? user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return Results.NotFound();
        }

        string[] requestedRoles = request.Roles
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string[] unsupportedRoles = requestedRoles
            .Except(ApplicationRoleNames.All, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (unsupportedRoles.Length > 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(UpdateUserRolesRequest.Roles)] =
                [
                    $"Unsupported role(s): {string.Join(", ", unsupportedRoles)}."
                ]
            });
        }

        IList<string> currentRoles = await userManager.GetRolesAsync(user);
        string[] rolesToRemove = currentRoles
            .Except(requestedRoles, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] rolesToAdd = requestedRoles
            .Except(currentRoles, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (rolesToRemove.Length > 0)
        {
            IdentityResult removeResult = await userManager.RemoveFromRolesAsync(user, rolesToRemove);
            if (!removeResult.Succeeded)
            {
                return Results.ValidationProblem(ToValidationErrors(removeResult));
            }
        }

        if (rolesToAdd.Length > 0)
        {
            IdentityResult addResult = await userManager.AddToRolesAsync(user, rolesToAdd);
            if (!addResult.Succeeded)
            {
                return Results.ValidationProblem(ToValidationErrors(addResult));
            }
        }

        IList<string> updatedRoles = await userManager.GetRolesAsync(user);
        return Results.Ok(ToUserManagementSurfaceSummary(user, updatedRoles));
    }

    private static async Task<IResult> UpdateUserActivation(
        string userId,
        UpdateUserActivationRequest? request,
        HttpContext httpContext,
        IServiceProvider serviceProvider)
    {
        if (request is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(UpdateUserActivationRequest.IsActive)] = ["An activation request body is required."]
            });
        }

        if (request.IsActive is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(UpdateUserActivationRequest.IsActive)] = ["The activation state is required."]
            });
        }

        UserManager<ApplicationUser>? userManager = serviceProvider.GetService<UserManager<ApplicationUser>>();
        if (userManager is null)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        ApplicationUser? user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return Results.NotFound();
        }

        bool requestedIsActive = request.IsActive.Value;
        ApplicationUser? signedInUser = await SystemUptimeTrackerAuthorizationClaims.ResolveLinkedUserAsync(userManager, httpContext.User);

        if (!requestedIsActive
            && signedInUser is not null
            && string.Equals(signedInUser.Id, user.Id, StringComparison.OrdinalIgnoreCase))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(UpdateUserActivationRequest.IsActive)] = ["Administrators cannot deactivate their own account."]
            });
        }

        if (!requestedIsActive
            && await userManager.IsInRoleAsync(user, ApplicationRoleNames.ADMIN)
            && !await HasAnotherActiveAdminAsync(userManager, user.Id))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(UpdateUserActivationRequest.IsActive)] = ["At least one active administrator must remain."]
            });
        }

        if (user.IsActive != requestedIsActive)
        {
            user.IsActive = requestedIsActive;

            IdentityResult updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                return Results.ValidationProblem(ToValidationErrors(updateResult));
            }

            IdentityResult securityStampResult = await userManager.UpdateSecurityStampAsync(user);
            if (!securityStampResult.Succeeded)
            {
                return Results.ValidationProblem(ToValidationErrors(securityStampResult));
            }
        }

        IList<string> roles = await userManager.GetRolesAsync(user);
        return Results.Ok(ToUserManagementSurfaceSummary(user, roles));
    }

    private static async Task<bool> HasAnotherActiveAdminAsync(UserManager<ApplicationUser> userManager, string excludedUserId)
    {
        IList<ApplicationUser> admins = await userManager.GetUsersInRoleAsync(ApplicationRoleNames.ADMIN);
        return admins.Any(admin =>
            admin.IsActive &&
            !string.Equals(admin.Id, excludedUserId, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, string[]> ToValidationErrors(IdentityResult result)
    {
        return result.Errors
            .GroupBy(error => error.Code)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Description).ToArray());
    }

    private static UserManagementSurfaceSummary ToUserManagementSurfaceSummary(ApplicationUser user, IEnumerable<string> roles)
    {
        return new UserManagementSurfaceSummary(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            roles.OrderBy(role => role, StringComparer.OrdinalIgnoreCase).ToArray(),
            user.IsActive,
            user.CreatedAtUtc,
            user.LastLoginAtUtc);
    }
}
