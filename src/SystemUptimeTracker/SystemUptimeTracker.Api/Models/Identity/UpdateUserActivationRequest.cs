using System.ComponentModel.DataAnnotations;

namespace SystemUptimeTracker.Api.Models.Identity;

public sealed class UpdateUserActivationRequest
{
    [Required]
    public bool? IsActive { get; set; }
}