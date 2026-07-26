using Microsoft.Extensions.Compliance.Classification;
using System.Diagnostics.CodeAnalysis;

namespace SystemUptimeTracker.Common.Helpers.Data;

[ExcludeFromCodeCoverage]
public class SensitiveDataAttribute : DataClassificationAttribute
{
    // ReSharper disable once ConvertToPrimaryConstructor
    public SensitiveDataAttribute() : base(DataTaxonomy.SensitiveData)
    {
    }
}