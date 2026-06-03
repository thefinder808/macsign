using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MacSign.App.Services;

namespace MacSign.App.Tests;

/// <summary>A fake <see cref="IProcessRunner"/> shared by the App tests: records
/// every call and returns whatever <see cref="Respond"/> decides.</summary>
internal sealed class FakeRunner : IProcessRunner
{
    public readonly List<(string File, List<string> Args)> Calls = new();
    public Func<string, IReadOnlyList<string>, ProcessResult> Respond =
        (_, _) => new ProcessResult(0, "", "", false);

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> args,
        IProgress<string>? onOutput, CancellationToken ct)
    {
        Calls.Add((fileName, args.ToList()));
        return Task.FromResult(Respond(fileName, args));
    }
}
