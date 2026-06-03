namespace MacSign.App.ViewModels;

/// <summary>Credential mode shown in the Sign inspector's segmented control.
/// Maps to the engine's <c>CertMode</c>.</summary>
public enum CredMode
{
    Pfx,
    Pkcs11,
    Azure,
}

/// <summary>Per-file progress during a signing run.</summary>
public enum FileRunState
{
    None,
    Signing,
    Done,
}

/// <summary>Overall state of the Sign screen.</summary>
public enum SignState
{
    Idle,
    Signing,
    Done,
}

/// <summary>Overall state of the Mac-app signing screen.</summary>
public enum AppleSignState
{
    Idle,
    Working,
    Done,
}
