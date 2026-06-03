using System;
using System.IO;
using MacSign.App.Services;
using Xunit;

namespace MacSign.App.Tests;

public class EngineRemoveTests
{
    [Fact]
    public void Remove_is_no_throw_on_unsupported_file()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "macsign-rm-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(tmp, "not a signable file");

        var r = new EngineService().Remove(tmp);

        Assert.False(r.Removed);
        Assert.NotNull(r.Error); // NotSupportedException captured, not thrown
    }
}
