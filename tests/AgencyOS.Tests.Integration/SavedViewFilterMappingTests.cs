using System.Reflection;
using AgencyOS.Api.Endpoints;
using AgencyOS.Contracts.SavedViews;
using AgencyOS.Domain.SavedViews;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// Every saved-view filter survives the trip between the wire and the domain.
/// </summary>
/// <remarks>
/// <para>
/// This test exists because the same defect shipped twice. M4 added eight filters
/// and the API carried none of them: the two records have identical shapes, so a
/// positional mapping compiled and silently defaulted everything. The fix was
/// named arguments - which stops values landing in the wrong field, and does
/// nothing about a field left out, because every filter is optional. M5 added
/// seven more and forgot all seven.
/// </para>
/// <para>
/// Two identical record shapes mapped by hand cannot be made safe by care. So the
/// property list is walked by reflection instead: each one is set to a distinctive
/// value, pushed through both mappings, and read back. A filter added later that
/// nobody wires up fails here immediately, whatever it is called.
/// </para>
/// </remarks>
public sealed class SavedViewFilterMappingTests
{
    /// <summary>Every contract filter reaches the domain.</summary>
    [Fact]
    public void EveryFilter_SurvivesTheTripToTheDomain()
    {
        SavedViewFiltersModel populated = Populate<SavedViewFiltersModel>();

        SavedViewFilters mapped = M3Endpoints.ToFilters(populated);

        AssertEveryPropertyCarried(populated, mapped);
    }

    /// <summary>And every domain filter comes back out on the wire.</summary>
    /// <remarks>
    /// The return trip matters as much: a filter the server stores but never echoes
    /// is one the user cannot see they saved, and cannot correct.
    /// </remarks>
    [Fact]
    public void EveryFilter_SurvivesTheTripBack()
    {
        SavedViewFilters populated = Populate<SavedViewFilters>();

        SavedViewFiltersModel mapped = M3Endpoints.ToModel(populated);

        AssertEveryPropertyCarried(populated, mapped);
    }

    /// <summary>The two records stay the same shape, so neither grows alone.</summary>
    [Fact]
    public void TheContractAndTheDomain_DescribeTheSameFilters()
    {
        string[] contract = [.. Properties<SavedViewFiltersModel>().Select(x => x.Name).Order()];
        string[] domain = [.. Properties<SavedViewFilters>().Select(x => x.Name).Order()];

        Assert.Equal(domain, contract);
    }

    /// <summary>
    /// Fills every property with a value distinguishable from its default.
    /// </summary>
    /// <remarks>
    /// The values are deliberately distinct per property, so a mapping that copies
    /// the right number of fields into the wrong slots is caught as well as one
    /// that drops them.
    /// </remarks>
    private static T Populate<T>()
    {
        ConstructorInfo constructor = typeof(T).GetConstructors().Single();

        ParameterInfo[] parameters = constructor.GetParameters();
        object?[] arguments = new object?[parameters.Length];

        for (int index = 0; index < parameters.Length; index++)
        {
            arguments[index] = Sample(parameters[index].ParameterType, index);
        }

        return (T)constructor.Invoke(arguments);
    }

    private static object? Sample(Type type, int seed) => type switch
    {
        _ when type == typeof(string) => $"value-{seed}",
        _ when type == typeof(bool) => true,
        _ when type == typeof(int?) => seed + 1,
        _ when type == typeof(Guid?) => Deterministic(seed),
        _ => throw new NotSupportedException(
            $"A saved-view filter of type {type} has no sample value. Add one here so the "
                + "round-trip stays total."),
    };

    /// <summary>A distinct, reproducible identifier per property position.</summary>
    private static Guid Deterministic(int seed)
    {
        byte[] bytes = new byte[16];
        bytes[0] = (byte)(seed + 1);

        return new Guid(bytes);
    }

    private static void AssertEveryPropertyCarried(object source, object target)
    {
        foreach (PropertyInfo property in source.GetType().GetProperties())
        {
            PropertyInfo? counterpart = target.GetType().GetProperty(property.Name);

            Assert.True(
                counterpart is not null,
                $"'{property.Name}' has no counterpart on {target.GetType().Name}.");

            object? expected = property.GetValue(source);
            object? actual = counterpart!.GetValue(target);

            Assert.True(
                Equals(expected, actual),
                $"Filter '{property.Name}' did not survive the mapping: expected '{expected}', "
                    + $"got '{actual}'. Wire it up in M3Endpoints.");
        }
    }

    private static IEnumerable<PropertyInfo> Properties<T>() =>
        typeof(T).GetProperties().Where(x => x.Name != "EqualityContract");
}
