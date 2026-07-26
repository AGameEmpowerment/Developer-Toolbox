using SystemUptimeTracker.Api.Factories.Lookups;
using SystemUptimeTracker.Api.Models.Platform.Lookups;
using SystemUptimeTracker.Api.Models.Ui.Lookups;

namespace SystemUptimeTracker.Tests.Factories.Lookups;

[TestFixture(Category = "Unit")]
public class CountryFactoryTests
{
    private CountryFactory _countryFactory;

    [SetUp]
    public void Setup()
    {
        _countryFactory = new CountryFactory();
    }

    [Test]
    public void ToUiReturnsNullWhenGeopoliticalLocationIsNull()
    {
        UiCountry? result = _countryFactory.ToUi(null);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ToUiMapsProperties()
    {
        GeopoliticalLocation geopoliticalLocation = new GeopoliticalLocation()
        {
            Id = 1,
            IsActive = true,
            CommonName = "Test Name",
            AbbreviatedName = "TN",
            CountryDialingCode = 123
        };
        UiCountry? result = _countryFactory.ToUi(geopoliticalLocation);
        Assert.That(result, Is.Not.Null);
        UiCountry actual = result!;
        Assert.That(actual.Id, Is.EqualTo(geopoliticalLocation.Id));
        Assert.That(actual.IsActive, Is.EqualTo(geopoliticalLocation.IsActive));
        Assert.That(actual.Name, Is.EqualTo(geopoliticalLocation.CommonName));
        Assert.That(actual.AbbreviatedName, Is.EqualTo(geopoliticalLocation.AbbreviatedName));
        Assert.That(actual.CountryDialingCode, Is.EqualTo(geopoliticalLocation.CountryDialingCode));
    }
}
