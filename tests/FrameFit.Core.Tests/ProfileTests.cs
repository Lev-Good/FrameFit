using FrameFit.Core.Geometry;
using FrameFit.Core.Profiles;
using Xunit;

namespace FrameFit.Core.Tests;

public class DisplayFingerprintTests
{
    private static readonly DisplayFingerprint Reference = new("\\\\.\\DISPLAY2", "ABCD1234", "HDMI", 1920, 1080);

    [Fact]
    public void MatchScore_IsMaximalForAnIdenticalDisplay()
    {
        Assert.Equal(100, Reference.MatchScore(Reference));
    }

    [Fact]
    public void MatchScore_StillMatchesWhenOnlyTheDeviceNameChanged()
    {
        var movedToAnotherPort = Reference with { DeviceName = "\\\\.\\DISPLAY3" };

        Assert.Equal(80, Reference.MatchScore(movedToAnotherPort));
        Assert.True(Reference.IsMatch(movedToAnotherPort));
    }

    [Fact]
    public void MatchScore_DoesNotMatchWithoutTheEdid()
    {
        var other = Reference with { EdidHash = string.Empty };

        Assert.False(Reference.IsMatch(other));
    }

    [Fact]
    public void MatchScore_IgnoresCaseInIdentifiers()
    {
        var lower = Reference with { EdidHash = "abcd1234", OutputTechnology = "hdmi" };

        Assert.Equal(100, Reference.MatchScore(lower));
    }

    [Fact]
    public void Unknown_DoesNotMatchAnything()
    {
        Assert.False(DisplayFingerprint.Unknown.IsMatch(Reference));
        Assert.Equal(0, DisplayFingerprint.Unknown.MatchScore(Reference));
        Assert.False(DisplayFingerprint.Unknown.IsAcceptableMatch(Reference));
    }

    [Fact]
    public void IsAcceptableMatch_FallsBackToDeviceNameAndResolution()
    {
        // המקרה שבו ה-EDID אינו זמין: שמירה בלי hash עדיין חייבת לחזור לעצמה.
        var withoutEdid = Reference with { EdidHash = string.Empty };
        var current = Reference with { EdidHash = string.Empty };

        Assert.True(withoutEdid.IsAcceptableMatch(current));
    }

    [Fact]
    public void IsAcceptableMatch_RejectsDifferentResolutionOnTheSamePort()
    {
        var stored = Reference with { EdidHash = string.Empty };
        var current = Reference with { EdidHash = string.Empty, Width = 1280, Height = 720 };

        Assert.False(stored.IsAcceptableMatch(current));
    }

    [Fact]
    public void IsAcceptableMatch_RejectsADifferentPort()
    {
        var stored = Reference with { EdidHash = string.Empty };
        var current = Reference with { EdidHash = string.Empty, DeviceName = "\\\\.\\DISPLAY3" };

        Assert.False(stored.IsAcceptableMatch(current));
    }
}

public class ProfileMatcherTests
{
    private static readonly DisplayFingerprint Display = new("\\\\.\\DISPLAY2", "ABCD1234", "HDMI", 1920, 1080);

    [Fact]
    public void FindBest_PicksTheHighestScoringProfile()
    {
        var document = new SettingsDocument();
        document.Profiles.Add(new FrameFitProfile
        {
            Id = "weak",
            Display = Display with { EdidHash = "ZZZZ" }
        });
        document.Profiles.Add(new FrameFitProfile
        {
            Id = "strong",
            Display = Display
        });

        var match = ProfileMatcher.FindBest(document, Display);

        Assert.NotNull(match);
        Assert.Equal("strong", match!.Id);
    }

    [Fact]
    public void FindBest_ReturnsNullWhenNothingMatches()
    {
        var document = new SettingsDocument();
        document.Profiles.Add(new FrameFitProfile { Id = "other", Display = new("\\\\.\\DISPLAY9", "FFFF", "VGA", 1024, 768) });

        Assert.Null(ProfileMatcher.FindBest(document, Display));
    }

    [Fact]
    public void FindBest_MatchesWithoutEdidOnTheSameOutputAndResolution()
    {
        // תרחיש אמיתי: הקריאה של ה-EDID מהרישום נכשלה, אך אותה יציאה ואותה רזולוציה.
        var document = new SettingsDocument();
        document.Profiles.Add(new FrameFitProfile
        {
            Id = "saved",
            Display = Display with { EdidHash = string.Empty }
        });

        var match = ProfileMatcher.FindBest(document, Display with { EdidHash = string.Empty });

        Assert.NotNull(match);
        Assert.Equal("saved", match!.Id);
    }

    [Fact]
    public void FindBest_PrefersTheEdidMatchOverTheFallback()
    {
        var document = new SettingsDocument();
        document.Profiles.Add(new FrameFitProfile
        {
            Id = "fallback",
            Display = Display with { EdidHash = string.Empty }
        });
        document.Profiles.Add(new FrameFitProfile
        {
            Id = "exact",
            Display = Display
        });

        var match = ProfileMatcher.FindBest(document, Display);

        Assert.Equal("exact", match!.Id);
    }

    [Fact]
    public void Upsert_ReplacesAnExistingProfileWithTheSameId()
    {
        var document = new SettingsDocument();
        ProfileMatcher.Upsert(document, new FrameFitProfile { Id = "a", Margins = new MarginSet(1, 1, 1, 1) });
        ProfileMatcher.Upsert(document, new FrameFitProfile { Id = "a", Margins = new MarginSet(2, 2, 2, 2) });

        Assert.Single(document.Profiles);
        Assert.Equal(new MarginSet(2, 2, 2, 2), document.Profiles[0].Margins);
    }

    [Fact]
    public void BuildId_IsStableAndFileSystemSafe()
    {
        var first = ProfileMatcher.BuildId(Display);
        var second = ProfileMatcher.BuildId(Display);

        Assert.Equal(first, second);
        Assert.DoesNotContain('\\', first);
        Assert.DoesNotContain('.', first);
    }

    [Fact]
    public void BuildId_FallsBackToDeviceNameWithoutEdid()
    {
        var id = ProfileMatcher.BuildId(Display with { EdidHash = string.Empty });

        Assert.StartsWith("display2", id);
    }
}

public class ProfileStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "framefit-tests-" + Guid.NewGuid().ToString("N"));

    private string SettingsFile => Path.Combine(_directory, "settings.json");

    [Fact]
    public void SaveAndLoad_RoundTripsMarginsAndOptions()
    {
        var store = new ProfileStore(SettingsFile);
        var document = new SettingsDocument { StartWithWindows = true, ActiveProfileId = "p1" };
        document.Profiles.Add(new FrameFitProfile
        {
            Id = "p1",
            Display = new DisplayFingerprint("\\\\.\\DISPLAY2", "ABCD1234", "HDMI", 1920, 1080),
            Margins = new MarginSet(38, 64, 22, 14),
            Options = new ProfileOptions { ReserveWorkArea = true, ClampCursor = false }
        });

        store.Save(document);
        var loaded = store.Load(out var wasReset);

        Assert.False(wasReset);
        Assert.True(loaded.StartWithWindows);
        var profile = Assert.Single(loaded.Profiles);
        Assert.Equal(new MarginSet(38, 64, 22, 14), profile.Margins);
        Assert.True(profile.Options.ReserveWorkArea);
        Assert.False(profile.Options.ClampCursor);
    }

    [Fact]
    public void Load_FromASettingsFileWithTheLegacyHideTaskbarFlag_KeepsTheProfile()
    {
        // קובץ הגדרות שנשמר בגרסה קודמת: יש בו HideTaskbar, שכבר אינו קיים, ואין בו
        // TaskbarMode. השדה הישן אינו אמור להפיל את הטעינה ולא לאפס את ההגדרות —
        // אחרת המשתמש מאבד את השוליים המדודים שלו בשדרוג.
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsFile,
            "{\"SchemaVersion\":1,\"ActiveProfileId\":\"p1\",\"Profiles\":[{\"Id\":\"p1\"," +
            "\"Margins\":{\"Left\":196,\"Right\":213,\"Top\":37,\"Bottom\":410}," +
            "\"Options\":{\"BlackoutMargins\":true,\"HideTaskbar\":true}}]}");

        var store = new ProfileStore(SettingsFile);
        var loaded = store.Load(out var wasReset);

        Assert.False(wasReset);
        var profile = Assert.Single(loaded.Profiles);
        Assert.Equal(new MarginSet(196, 213, 37, 410), profile.Margins);

        // ברירת המחדל הנוכחית חלה עליו — הסרגל לא נחשב מוסתר, וגם לא מנוסה להעברה
        // שאינה אפשרית ב-Windows 11.
        Assert.Equal(TaskbarMode.LeaveInPlace, profile.Options.TaskbarMode);
    }

    [Fact]
    public void Load_WithMissingFile_ReturnsDefaultAndReportsReset()
    {
        var store = new ProfileStore(SettingsFile);

        var loaded = store.Load(out var wasReset);

        Assert.True(wasReset);
        Assert.Empty(loaded.Profiles);
        Assert.False(AutostartFlag(loaded));
    }

    [Fact]
    public void Load_WithCorruptFile_ReturnsDefaultAndReportsReset()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsFile, "{ this is not json ");
        var store = new ProfileStore(SettingsFile);

        var loaded = store.Load(out var wasReset);

        Assert.True(wasReset);
        Assert.Empty(loaded.Profiles);
    }

    [Fact]
    public void Load_FromNewerSchema_IsRefusedRatherThanMisread()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsFile, "{\"SchemaVersion\":99,\"Profiles\":[]}");
        var store = new ProfileStore(SettingsFile);

        var loaded = store.Load(out var wasReset);

        Assert.True(wasReset);
        Assert.Equal(SettingsDocument.CurrentSchemaVersion, loaded.SchemaVersion);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static bool AutostartFlag(SettingsDocument document) => document.StartWithWindows;
}
