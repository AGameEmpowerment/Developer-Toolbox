using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SystemUptimeTracker.Api.Constants;
using SystemUptimeTracker.Api.Models.Identity;
using SystemUptimeTracker.Data.Identity;
using System.ComponentModel.DataAnnotations;

namespace SystemUptimeTracker.Api.Extensions;

public static class BootstrapIdentityEndpointRouteBuilderExtensions
{
    private static readonly SemaphoreSlim BootstrapCreateGate = new(1, 1);

    public static RouteGroupBuilder MapSystemUptimeTrackerBootstrapIdentityEndpoints(this RouteGroupBuilder identityGroup)
    {
        identityGroup.MapGet("/setup-status", GetSetupStatusAsync)
            .AllowAnonymous();

        identityGroup.MapPost("/self-create", SelfCreateUserAsync)
            .AllowAnonymous();

        identityGroup.MapPost("/bootstrap-admin", BootstrapAdminAsync)
            .AllowAnonymous();

        return identityGroup;
    }

    private static async Task<IResult> GetSetupStatusAsync(
        UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken)
    {
        bool hasUsers = await userManager.Users.AnyAsync(cancellationToken);
        bool hasAdministrators = await HasAdministratorsAsync(userManager, cancellationToken);
        bool isFirstTimeSetup = !hasUsers || !hasAdministrators;

        return Results.Ok(new IdentitySetupStatusResponse(
            hasUsers,
            hasAdministrators,
            isFirstTimeSetup,
            isFirstTimeSetup));
    }

    private static async Task<IResult> SelfCreateUserAsync(
        BootstrapAdminUserRequest? request,
        UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken)
    {
        if (!TryValidateBootstrapRequest(request, out Dictionary<string, string[]> validationErrors))
        {
            return Results.ValidationProblem(validationErrors);
        }

        BootstrapAdminUserRequest validatedRequest = request!;
        return await CreateAnonymousUserAsync(
            validatedRequest,
            userManager,
            cancellationToken);
    }

    private static async Task<IResult> BootstrapAdminAsync(
        BootstrapAdminUserRequest? request,
        UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken)
    {
        if (!TryValidateBootstrapRequest(request, out Dictionary<string, string[]> validationErrors))
        {
            return Results.ValidationProblem(validationErrors);
        }

        BootstrapAdminUserRequest validatedRequest = request!;
        return await CreateAnonymousUserAsync(
            validatedRequest,
            userManager,
            cancellationToken,
            forceAdministratorResponse: true);
    }

    private static bool TryValidateBootstrapRequest(
        BootstrapAdminUserRequest? request,
        out Dictionary<string, string[]> validationErrors)
    {
        validationErrors = [];

        if (request is null)
        {
            validationErrors[string.Empty] = ["A request body is required."];
            return false;
        }

        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(request);
        bool isValid = Validator.TryValidateObject(request, validationContext, validationResults, validateAllProperties: true);

        if (!isValid)
        {
            validationErrors = validationResults
                .SelectMany(result => result.MemberNames.DefaultIfEmpty(string.Empty), (result, memberName) => new
                {
                    MemberName = memberName,
                    ErrorMessage = result.ErrorMessage ?? "The value is invalid."
                })
                .GroupBy(error => error.MemberName)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.ErrorMessage).ToArray());
        }

        return isValid;
    }

    private static async Task<IdentityResult> AddMissingApplicationRolesAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationUser user)
    {
        IList<string> assignedRoles = await userManager.GetRolesAsync(user);
        string[] missingRoles = ApplicationRoleNames.All
            .Except(assignedRoles, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return missingRoles.Length == 0
            ? IdentityResult.Success
            : await userManager.AddToRolesAsync(user, missingRoles);
    }

    private static async Task<bool> HasAdministratorsAsync(
        UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken)
    {
        IList<ApplicationUser> administrators = await userManager.GetUsersInRoleAsync(ApplicationRoleNames.ADMIN);
        return administrators.Any(admin => admin.IsActive);
    }

    private static async Task<IResult> CreateAnonymousUserAsync(
        BootstrapAdminUserRequest request,
        UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken,
        bool forceAdministratorResponse = false)
    {
        await BootstrapCreateGate.WaitAsync(cancellationToken);

        try
        {
            bool hadExistingUsers = await userManager.Users.AnyAsync(cancellationToken);
            bool hadAdministrators = await HasAdministratorsAsync(userManager, cancellationToken);
            bool shouldAssignAdministratorRoles = !hadAdministrators;

            if (forceAdministratorResponse && !shouldAssignAdministratorRoles)
            {
                return Results.Conflict(new
                {
                    message = "Bootstrap administrator creation is only available when the database has no active administrator."
                });
            }

            string email = request.Email.Trim();
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                    ? email
                    : request.DisplayName.Trim(),
                IsActive = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            IdentityResult createResult = await userManager.CreateAsync(user, request.Password);
            if (!createResult.Succeeded)
            {
                return Results.ValidationProblem(ToValidationErrors(createResult));
            }

            if (shouldAssignAdministratorRoles)
            {
                IdentityResult rolesResult = await AddMissingApplicationRolesAsync(userManager, user);
                if (!rolesResult.Succeeded)
                {
                    return Results.ValidationProblem(ToValidationErrors(rolesResult));
                }
            }

            IList<string> assignedRoles = await userManager.GetRolesAsync(user);
            string[] orderedRoles = assignedRoles
                .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (forceAdministratorResponse)
            {
                return Results.Created(
                    "/api/identity/manage/info",
                    new BootstrapAdminUserResponse(user.Id, email, user.DisplayName, orderedRoles));
            }

            bool isAdministrativeSetup = shouldAssignAdministratorRoles;
            return Results.Created(
                "/api/identity/manage/info",
                new SelfCreateUserResponse(
                    user.Id,
                    email,
                    user.DisplayName,
                    isAdministrativeSetup,
                    !isAdministrativeSetup,
                    orderedRoles));
        }
        finally
        {
            BootstrapCreateGate.Release();
        }
    }

    private static Dictionary<string, string[]> ToValidationErrors(IdentityResult result)
    {
        return result.Errors
            .GroupBy(error => error.Code)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Description).ToArray());
    }
}
