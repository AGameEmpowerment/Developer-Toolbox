using SystemUptimeTracker.Api.Models.Platform.Lookups;
using SystemUptimeTracker.Api.Models.Ui.Lookups;

namespace SystemUptimeTracker.Api.Factories.Lookups.Interfaces;

public interface ICountryFactory
{
    UiCountry? ToUi(GeopoliticalLocation? geopoliticalLocation);
}
