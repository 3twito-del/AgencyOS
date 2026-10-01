using AgencyOS.Canonical.Detector.Output;
using Xunit;

namespace AgencyOS.Tests.Canonical;

/// <summary>The packet serialisation is RFC 8785 for the values the detector writes.</summary>
public sealed class CanonicalJsonTests
{
    [Fact]
    public void KeysAreOrderedByCodeUnitAndThereIsNoWhitespace()
    {
        JsonObject value = new()
        {
            ["b"] = "2",
            ["a"] = new List<object> { true, false, "x" },
            ["B"] = new JsonObject { ["z"] = "1", ["y"] = "0" },
        };

        Assert.Equal("{\"B\":{\"y\":\"0\",\"z\":\"1\"},\"a\":[true,false,\"x\"],\"b\":\"2\"}", CanonicalJson.Serialize(value));
    }

    [Fact]
    public void StringsAreEscapedAsECMAScriptDoes()
    {
        Assert.Equal("\"q\\\"b\\\\n\\nt\\t\\u0001é—\"", CanonicalJson.Serialize("q\"b\\n\nt\t\u0001é—"));
    }

    [Fact]
    public void NumbersAreRefused()
    {
        Assert.Throws<InvalidOperationException>(() => CanonicalJson.Serialize(new JsonObject { ["n"] = 1 }));
    }
}
