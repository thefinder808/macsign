using System;
using MacSign.App.Services;

namespace MacSign.App.ViewModels;

/// <summary>One row in the Activity log.</summary>
public sealed class RunItemViewModel
{
    public RunData Data { get; }
    public RunItemViewModel(RunData data) => Data = data;

    public string FilesText => $"{Data.FileCount} file{(Data.FileCount == 1 ? "" : "s")}";
    public string DetailText => $"{Data.Credential} · {Data.Detail}";
    public string WhenText => Format(Data.WhenIso);

    public bool IsOk => Data.Status == "ok";
    public bool IsWarn => Data.Status == "warn";
    public bool IsFail => Data.Status == "fail";

    private static string Format(string iso)
    {
        if (!DateTime.TryParse(iso, out var d)) return "";
        var local = d.ToLocalTime();
        var today = DateTime.Now.Date;
        if (local.Date == today) return $"Today · {local:HH:mm}";
        if (local.Date == today.AddDays(-1)) return $"Yesterday · {local:HH:mm}";
        return $"{local:MMM d} · {local:HH:mm}";
    }
}
