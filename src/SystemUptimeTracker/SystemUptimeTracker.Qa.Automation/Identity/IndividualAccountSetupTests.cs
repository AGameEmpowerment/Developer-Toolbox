using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemUptimeTracker.Data.Identity;
using SystemUptimeTracker.Qa.Automation.Services;
using SystemUptimeTracker.Qa.Automation.Support;
using SystemUptimeTracker.Qa.Automation.TestBases;

namespace SystemUptimeTracker.Qa.Automation.Identity;

[TestFixture(Category = "Automation"), Category("Integration"), Category("Identity"), Category("Setup")]
public sealed class IndividualAccountSetupTests : SystemUptimeTrackerFunctionalTestBase
{
    [Test]
    public async Task EnsureIndividualAccountReady_CreatesScopedEphemeralLocalUser()
    {
        Logger.LogInformation("Ensuring the scoped ephemeral local individual-account identity is ready for automation use.");
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        ITestIdentityAccountProvisioningService provisioningService = scope.ServiceProvider
            .GetRequiredService<ITestIdentityAccountProvisioningService>();
        LocalIdentityTestUserConfiguration localIdentitySettings = scope.ServiceProvider
            .GetRequiredService<IOptions<AutomationAppSettings>>()
            .Value
            .LocalIdentityTestUser;
        string expectedEmailDomain = localIdentitySettings.EmailDomain.Trim().TrimStart('@');

        TestIdentityAccountProvisioningResult firstPass = await provisioningService.EnsureIndividualAccountReadyAsync();
        TestIdentityAccountProvisioningResult secondPass = await provisioningService.EnsureIndividualAccountReadyAsync();

        Assert.Multiple(() =>
        {
            Assert.That(firstPass.Email, Is.Not.Empty);
            Assert.That(firstPass.Email, Does.EndWith($"@{expectedEmailDomain}"));
            Assert.That(firstPass.Password, Is.Not.Empty);
            Assert.That(firstPass.EmailConfirmed, Is.True);
            Assert.That(firstPass.SignInValidated, Is.True);
            Assert.That(firstPass.CleanupScheduled, Is.True);
            Assert.That(firstPass.AssignedRoles, Contains.Item("Admin"));
            Assert.That(firstPass.AssignedRoles, Is.SupersetOf(firstPass.RequiredRoles));

            Assert.That(secondPass.Email, Is.EqualTo(firstPass.Email));
            Assert.That(secondPass.Password, Is.EqualTo(firstPass.Password));
            Assert.That(secondPass.UserCreated, Is.False);
            Assert.That(secondPass.PasswordReset, Is.False);
            Assert.That(secondPass.EmailConfirmed, Is.True);
            Assert.That(secondPass.SignInValidated, Is.True);
            Assert.That(secondPass.CleanupScheduled, Is.True);
            Assert.That(secondPass.AssignedRoles, Contains.Item("Admin"));
            Assert.That(secondPass.AssignedRoles, Is.SupersetOf(secondPass.RequiredRoles));
        });

        Logger.LogInformation(
            "The scoped ephemeral local individual-account identity {Email} is ready with roles: {Roles}.",
            secondPass.Email,
            string.Join(", ", secondPass.AssignedRoles));
    }

    [Test]
    public async Task EnsureIndividualAccountReady_DeletesDisposableLocalUser_WhenScopeIsDisposed()
    {
        string createdEmail;

        await using (AsyncServiceScope provisioningScope = Services.CreateAsyncScope())
        {
            ITestIdentityAccountProvisioningService provisioningService = provisioningScope.ServiceProvider
                .GetRequiredService<ITestIdentityAccountProvisioningService>();

            TestIdentityAccountProvisioningResult provisioningResult = await provisioningService.EnsureIndividualAccountReadyAsync();

            Assert.That(provisioningResult.CleanupScheduled, Is.True);
            createdEmail = provisioningResult.Email;
        }

        await using AsyncServiceScope verificationScope = Services.CreateAsyncScope();
        UserManager<ApplicationUser> userManager = verificationScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser? deletedUser = await userManager.FindByEmailAsync(createdEmail);

        Assert.That(deletedUser, Is.Null, "Disposable automation identities should be removed when the test scope ends.");
    }

    [Test]
    public async Task QaIdentityCleanup_DeletesProvisionedUsers_AndPreservesNonQaUsers()
    {
        const string QA_ARTIFACT_EMAIL = "systemuptimetracker-qa+cleanup@example.invalid";
        const string RETAINED_USER_EMAIL = "retained-user@example.invalid";

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ITestIdentityAccountCleanupService cleanupService = scope.ServiceProvider.GetRequiredService<ITestIdentityAccountCleanupService>();

        ApplicationUser? existingQaArtifact = await userManager.FindByEmailAsync(QA_ARTIFACT_EMAIL);
        if (existingQaArtifact is not null)
        {
            IdentityResult deleteExistingQaResult = await userManager.DeleteAsync(existingQaArtifact);
            Assert.That(deleteExistingQaResult.Succeeded, Is.True, string.Join("; ", deleteExistingQaResult.Errors.Select(error => error.Description)));
        }

        ApplicationUser? existingRetainedUser = await userManager.FindByEmailAsync(RETAINED_USER_EMAIL);
        if (existingRetainedUser is not null)
        {
            IdentityResult deleteExistingRetainedResult = await userManager.DeleteAsync(existingRetainedUser);
            Assert.That(deleteExistingRetainedResult.Succeeded, Is.True, string.Join("; ", deleteExistingRetainedResult.Errors.Select(error => error.Description)));
        }

        var qaArtifact = new ApplicationUser
        {
            UserName = QA_ARTIFACT_EMAIL,
            Email = QA_ARTIFACT_EMAIL,
            EmailConfirmed = true,
            DisplayName = QA_ARTIFACT_EMAIL
        };
        var retainedUser = new ApplicationUser
        {
            UserName = RETAINED_USER_EMAIL,
            Email = RETAINED_USER_EMAIL,
            EmailConfirmed = true,
            DisplayName = RETAINED_USER_EMAIL
        };

        IdentityResult createQaResult = await userManager.CreateAsync(qaArtifact, "Password1!");
        IdentityResult createRetainedResult = await userManager.CreateAsync(retainedUser, "Password1!");
        Assert.That(createQaResult.Succeeded, Is.True, string.Join("; ", createQaResult.Errors.Select(error => error.Description)));
        Assert.That(createRetainedResult.Succeeded, Is.True, string.Join("; ", createRetainedResult.Errors.Select(error => error.Description)));

        try
        {
            int deletedCount = await cleanupService.DeleteProvisionedAccountsAsync();
            ApplicationUser? deletedQaArtifact = await userManager.FindByEmailAsync(QA_ARTIFACT_EMAIL);
            ApplicationUser? preservedRetainedUser = await userManager.FindByEmailAsync(RETAINED_USER_EMAIL);

            Assert.Multiple(() =>
            {
                Assert.That(deletedCount, Is.GreaterThanOrEqualTo(1));
                Assert.That(deletedQaArtifact, Is.Null);
                Assert.That(preservedRetainedUser, Is.Not.Null);
            });
        }
        finally
        {
            ApplicationUser? userToDelete = await userManager.FindByEmailAsync(RETAINED_USER_EMAIL);
            if (userToDelete is not null)
            {
                IdentityResult deleteResult = await userManager.DeleteAsync(userToDelete);
                Assert.That(deleteResult.Succeeded, Is.True, string.Join("; ", deleteResult.Errors.Select(error => error.Description)));
            }
        }
    }
}
