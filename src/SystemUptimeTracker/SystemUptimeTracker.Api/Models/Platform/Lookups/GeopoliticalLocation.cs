namespace SystemUptimeTracker.Api.Models.Platform.Lookups;

public class GeopoliticalLocation
{
    public string AbbreviatedName { get; set; } = string.Empty;

    public string AddressFormat { get; set; } = string.Empty;

    public int? ClassCode { get; set; }

    public string CommonName { get; set; } = string.Empty;

    public int? CountryDialingCode { get; set; }
    public string DefaultCurrencyCode { get; set; } = string.Empty;

    public int? DefaultCurrencyId { get; set; }
    public string DefaultCurrencyName { get; set; } = string.Empty;
    public int Id { get; set; }

    public bool IsActive { get; set; }

    public string IsoAlpha2 { get; set; } = string.Empty;
    public string IsoAlpha3 { get; set; } = string.Empty;
    public int? IsoNumeric { get; set; }

    public int? ParentId { get; set; }
    public string TypeDescription { get; set; } = string.Empty;

    public int? TypeId { get; set; }
}
