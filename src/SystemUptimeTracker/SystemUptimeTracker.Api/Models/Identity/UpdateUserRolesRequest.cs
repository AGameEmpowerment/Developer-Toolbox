using System.ComponentModel.DataAnnotations;

namespace SystemUptimeTracker.Api.Models.Identity;

public sealed class UpdateUserRolesRequest
{
    [Required]
    public IReadOnlyCollection<string> Roles { get; set; } = [];
}
