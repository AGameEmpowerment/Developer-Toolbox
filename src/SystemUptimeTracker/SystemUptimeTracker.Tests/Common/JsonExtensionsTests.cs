using Newtonsoft.Json.Linq;
using SystemUptimeTracker.Common.Helpers.Extensions;

namespace SystemUptimeTracker.Tests.Common;

[TestFixture(Category = "Unit")]
public class JsonExtensionsTests
{
    [Test]
    public async Task FromJsonAsync_ShouldDeserializeJsonString()
    {
        string json = "{\"Name\":\"John\", \"Age\":30}";
        Person result = await json.FromJsonAsync<Person>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Name, Is.EqualTo("John"));
        Assert.That(result.Age, Is.EqualTo(30));
    }

    [Test]
    public void FromJson_ShouldDeserializeJsonString()
    {
        string json = "{\"Name\":\"John\", \"Age\":30}";
        Person? result = json.FromJson<Person>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Name, Is.EqualTo("John"));
        Assert.That(result.Age, Is.EqualTo(30));
    }

    [Test]
    public async Task ToJsonAsync_ShouldSerializeObjectToJsonString()
    {
        Person person = new Person
        {
            Name = "John",
            Age = 30
        };
        string result = await person.ToJsonAsync();
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("\"Name\":\"John\""));
        Assert.That(result, Does.Contain("\"Age\":30"));
    }

    [Test]
    public void ToJson_ShouldSerializeObjectToJsonString()
    {
        Person person = new Person
        {
            Name = "John",
            Age = 30
        };
        string? result = person.ToJson();
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("\"Name\":\"John\""));
        Assert.That(result, Does.Contain("\"Age\":30"));
    }

    [Test]
    public async Task FromJsonToJObjectAsync_ShouldDeserializeJsonStringToJObject()
    {
        string json = "{\"Name\":\"John\", \"Age\":30}";
        JObject? result = await json.FromJsonToJObjectAsync();
        Assert.That(result, Is.Not.Null);
        JObject actual = result!;
        Assert.Multiple(() =>
        {
            Assert.That(actual["Name"]?.ToString(), Is.EqualTo("John"));
            Assert.That(actual["Age"]?.ToObject<int>(), Is.EqualTo(30));
        });
    }

    [Test]
    public void FromJsonToJObject_ShouldDeserializeJsonStringToJObject()
    {
        string json = "{\"Name\":\"John\", \"Age\":30}";
        JObject result = json.FromJsonToJObject();
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result["Name"]?.ToString(), Is.EqualTo("John"));
            Assert.That(result["Age"]?.ToObject<int>(), Is.EqualTo(30));
        });
    }

    [Test]
    public async Task FromJsonToJArrayAsync_ShouldDeserializeJsonStringToJArray()
    {
        string json = "[{\"Name\":\"John\", \"Age\":30}, {\"Name\":\"Jane\", \"Age\":25}]";
        JArray? result = await json.FromJsonToJArrayAsync();
        Assert.That(result, Is.Not.Null);
        JArray actual = result!;
        Assert.That(actual.Count, Is.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(actual[0]?["Name"]?.ToString(), Is.EqualTo("John"));
            Assert.That(actual[0]?["Age"]?.ToObject<int>(), Is.EqualTo(30));
            Assert.That(actual[1]?["Name"]?.ToString(), Is.EqualTo("Jane"));
            Assert.That(actual[1]?["Age"]?.ToObject<int>(), Is.EqualTo(25));
        });
    }

    [Test]
    public void FromJsonToJArray_ShouldDeserializeJsonStringToJArray()
    {
        string json = "[{\"Name\":\"John\", \"Age\":30}, {\"Name\":\"Jane\", \"Age\":25}]";
        JArray result = json.FromJsonToJArray();
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(result[0]?["Name"]?.ToString(), Is.EqualTo("John"));
            Assert.That(result[0]?["Age"]?.ToObject<int>(), Is.EqualTo(30));
            Assert.That(result[1]?["Name"]?.ToString(), Is.EqualTo("Jane"));
            Assert.That(result[1]?["Age"]?.ToObject<int>(), Is.EqualTo(25));
        });
    }

    public class Person
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }
}
