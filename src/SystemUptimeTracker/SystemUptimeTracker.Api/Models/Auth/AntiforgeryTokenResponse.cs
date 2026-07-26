namespace SystemUptimeTracker.Api.Models.Auth;

public sealed record AntiforgeryTokenResponse(string RequestToken, string HeaderName);
