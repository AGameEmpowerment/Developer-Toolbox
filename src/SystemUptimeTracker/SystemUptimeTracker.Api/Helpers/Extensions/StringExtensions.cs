namespace SystemUptimeTracker.Api.Helpers.Extensions;

public static class StringExtensions
{
    public static string Mask(this string s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return s;
        }

        return "********";
    }
}