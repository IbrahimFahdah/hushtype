namespace Hushtype.Platform;

// dwExtraInfo values on input we inject, so our own keyboard hook can recognize it.
internal static class InputTags
{
    public const nuint Hushtype = 0x48555348; // "HUSH"
    public const nuint Diagnostic = 0x48555354;
}
