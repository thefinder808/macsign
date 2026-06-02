namespace MacSign.Fixture;

/// <summary>
/// Nothing meaningful — this type exists only so the project compiles to a
/// real, unsigned managed PE that the signing tests and CI can target.
/// </summary>
public static class Hello
{
    public static string Greeting => "MacSign fixture — an unsigned PE to be Authenticode-signed.";
}
