namespace SystemUptimeTracker.Api.Models.Ui.Lookups;

public class UiCountry
{
    public string AbbreviatedName { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int? CountryDialingCode { get; set; }
    public int Id { get; set; }

    public bool IsActive { get; set; }
}
