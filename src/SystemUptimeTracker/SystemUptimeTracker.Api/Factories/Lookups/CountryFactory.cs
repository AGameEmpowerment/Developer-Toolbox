using SystemUptimeTracker.Api.Factories.Lookups.Interfaces;
using SystemUptimeTracker.Api.Models.Platform.Lookups;
using SystemUptimeTracker.Api.Models.Ui.Lookups;

namespace SystemUptimeTracker.Api.Factories.Lookups;

public class CountryFactory : ICountryFactory
{
    public UiCountry? ToUi(GeopoliticalLocation? geopoliticalLocation)
    {
        if (geopoliticalLocation is null)
        {
            return null;
        }

        return new UiCountry
        {
            Id = geopoliticalLocation.Id,
            IsActive = geopoliticalLocation.IsActive,
            Name = geopoliticalLocation.CommonName,
            AbbreviatedName = geopoliticalLocation.AbbreviatedName,
            CountryDialingCode = geopoliticalLocation.CountryDialingCode,
        };
    }
}
