using FrameFit.Core.Geometry;
using FrameFit.Core.Profiles;

namespace FrameFit.Core.Abstractions;

/// <summary>
/// תיאור מסך כפי שהוא מתקבל ממערכת ההפעלה.
/// </summary>
public sealed record DisplayInfo(
    string DeviceName,
    string FriendlyName,
    PixelRect Bounds,
    PixelRect WorkArea,
    bool IsPrimary,
    string OutputTechnology,
    string EdidHash,
    int? PhysicalWidthMm,
    int? PhysicalHeightMm)
{
    public DisplayFingerprint ToFingerprint() =>
        new(DeviceName, EdidHash, OutputTechnology, Bounds.Width, Bounds.Height);

    /// <summary>תיאור קצר להצגה בבחירת מסך.</summary>
    public string Describe()
    {
        var name = string.IsNullOrWhiteSpace(FriendlyName) ? DeviceName : FriendlyName;
        var primary = IsPrimary ? "ראשי" : "משני";
        return $"{name} — {Bounds.Width}×{Bounds.Height}, {OutputTechnology}, {primary}";
    }
}

/// <summary>ספק רשימת המסכים. המימוש בפועל נמצא בשכבת ה-Windows.</summary>
public interface IDisplayProvider
{
    IReadOnlyList<DisplayInfo> GetDisplays();
}
