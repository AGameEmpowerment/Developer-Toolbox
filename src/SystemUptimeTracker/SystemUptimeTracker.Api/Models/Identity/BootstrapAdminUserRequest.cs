using System.ComponentModel.DataAnnotations;

namespace SystemUptimeTracker.Api.Models.Identity;

public sealed class BootstrapAdminUserRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    [MaxLength(256)]
    public string Password { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? DisplayName { get; set; }
}
