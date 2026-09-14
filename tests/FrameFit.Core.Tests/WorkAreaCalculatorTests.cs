using FrameFit.Core.Geometry;
using Xunit;

namespace FrameFit.Core.Tests;

public class WorkAreaCalculatorTests
{
    private static readonly PixelRect Monitor = new(0, 0, 1920, 1080);

    /// <summary>סרגל משימות תקני של Windows 11 בגובה 48 פיקסלים בתחתית המסך.</summary>
    private static readonly ReservedInsets Taskbar = new(0, 0, 0, 48);

    /// <summary>גובהו של אותו סרגל כאשר הוא נעגן בתוך האזור הגלוי.</summary>
    private const int TaskbarHeight = 48;

    [Fact]
    public void Plan_WithTaskbarAtBottom_EndsExactlyAtTheVisibleArea()
    {
        // המספרים של מסך אמיתי: שוליים לא-סימטריים עמוקים בתחתית, וסרגל משימות שקיים.
        var margins = new MarginSet(196, 213, 37, 410);

        var plan = WorkAreaCalculator.Plan(Monitor, margins, Taskbar);

        Assert.Equal(new PixelRect(196, 37, 1511, 633), plan.ExpectedWorkArea);
        Assert.Equal(plan.Visible, plan.ExpectedWorkArea);
        Assert.True(plan.IsExact);
        Assert.True(plan.Shortfall.IsZero);
    }

    [Fact]
    public void Plan_WithTaskbarAtBottom_ReservesOnlyTheRemainderOfTheBottomMargin()
    {
        var margins = new MarginSet(196, 213, 37, 410);

        var plan = WorkAreaCalculator.Plan(Monitor, margins, Taskbar);

        var bottom = plan.Strips.Single(s => s.Side == MarginSide.Bottom);
        Assert.Equal(new PixelRect(196, 670, 1511, 362), bottom.Rect);

        // איחוד הסרגל שלנו עם סרגל המשימות מכסה בדיוק את השוליים המוסתרים בתחתית.
        Assert.Equal(670, bottom.Rect.Y);
        Assert.Equal(Monitor.Bottom - margins.Bottom, bottom.Rect.Y);
        Assert.Equal(Monitor.Bottom, bottom.Rect.Bottom + Taskbar.Bottom);
    }

    [Fact]
    public void Plan_WithTaskbarReservesNothingOnTheOtherSides()
    {
        var plan = WorkAreaCalculator.Plan(Monitor, new MarginSet(196, 213, 37, 410), Taskbar);

        Assert.Equal(4, plan.Strips.Count);
        Assert.Equal(
            new PixelRect(0, 0, 196, 1080),
            plan.Strips.Single(s => s.Side == MarginSide.Left).Rect);
        Assert.Equal(
            new PixelRect(1707, 0, 213, 1080),
            plan.Strips.Single(s => s.Side == MarginSide.Right).Rect);
        Assert.Equal(
            new PixelRect(196, 0, 1511, 37),
            plan.Strips.Single(s => s.Side == MarginSide.Top).Rect);
    }

    [Fact]
    public void Plan_WithoutAnyExistingReservation_ReservesTheFullMargins()
    {
        var margins = new MarginSet(38, 64, 22, 14);

        var plan = WorkAreaCalculator.Plan(Monitor, margins, ReservedInsets.None);

        Assert.Equal(4, plan.Strips.Count);
        Assert.Equal(plan.Visible, plan.ExpectedWorkArea);
        Assert.Equal(new PixelRect(38, 22, 1818, 1044), plan.ExpectedWorkArea);
    }

    [Fact]
    public void Plan_WhenTheTaskbarIsTallerThanTheMargin_ReportsTheShortfall()
    {
        // שוליים של 20 פיקסלים בתחתית, וסרגל משימות של 48: 28 פיקסלים נגזלים מהאזור הגלוי.
        var plan = WorkAreaCalculator.Plan(Monitor, new MarginSet(0, 0, 0, 20), Taskbar);

        Assert.DoesNotContain(plan.Strips, s => s.Side == MarginSide.Bottom);
        Assert.Equal(28, plan.Shortfall.Bottom);
        Assert.False(plan.IsExact);
        Assert.Equal(1060, plan.Visible.Bottom);
        Assert.Equal(1032, plan.ExpectedWorkArea.Bottom);
        Assert.True(WorkAreaCalculator.StaysInside(plan, plan.ExpectedWorkArea));
    }

    [Fact]
    public void Plan_WithAnchoredTaskbar_EndsTheContentAboveTheTaskbar()
    {
        var margins = new MarginSet(196, 213, 37, 410);

        var plan = WorkAreaCalculator.Plan(Monitor, margins, Taskbar, TaskbarHeight);

        // הסרגל נעגן בתחתית האזור הגלוי, ולכן התוכן מסתיים מעליו ולא מתחתיו.
        Assert.Equal(new PixelRect(196, 37, 1511, 585), plan.ExpectedWorkArea);
        Assert.Equal(plan.Visible.Bottom - TaskbarHeight, plan.ExpectedWorkArea.Bottom);
        Assert.Equal(TaskbarHeight, plan.BottomAnchor);
        Assert.False(plan.IsExact);

        // המקום של הסרגל המעוגן אינו "גזילה" — הוא בשימוש מכוון ונראה לעין.
        Assert.Equal(0, plan.Shortfall.Bottom);
    }

    [Fact]
    public void Plan_WithAnchoredTaskbar_CoversTheAnchoredBandPlusTheHiddenMargin()
    {
        var margins = new MarginSet(196, 213, 37, 410);

        var plan = WorkAreaCalculator.Plan(Monitor, margins, Taskbar, TaskbarHeight);

        var bottom = plan.Strips.Single(s => s.Side == MarginSide.Bottom);

        Assert.Equal(plan.Visible.Bottom - TaskbarHeight, bottom.Rect.Y);
        Assert.Equal(Monitor.Bottom - Taskbar.Bottom, bottom.Rect.Bottom);
        Assert.Equal(Monitor.Bottom, bottom.Rect.Bottom + Taskbar.Bottom);
    }

    [Fact]
    public void Plan_WithAnchoredTaskbarAndShallowMargin_LosesNothingToTheTaskbar()
    {
        // שוליים של 20 פיקסלים בלבד בתחתית, וסרגל של 48 פיקסלים שנעגן מעליהם.
        var margins = new MarginSet(0, 0, 0, 20);

        var plan = WorkAreaCalculator.Plan(Monitor, margins, Taskbar, TaskbarHeight);

        Assert.Equal(new PixelRect(0, 0, 1920, 1012), plan.ExpectedWorkArea);
        Assert.Equal(0, plan.Shortfall.Bottom);

        // התוכן, הסרגל המעוגן והשוליים המוסתרים מכסים יחד את המסך עד לפיקסל האחרון.
        Assert.Equal(Monitor.Bottom, plan.ExpectedWorkArea.Bottom + TaskbarHeight + margins.Bottom);
    }

    [Fact]
    public void Plan_WithAnchoredTaskbar_WhenTheSystemAlreadyReservedItsBand_AddsNoStrip()
    {
        // ה-Shell כבר רשם את מלוא ההקצאה: השוליים (410) ועוד הסרגל המעוגן (48).
        var reserved = new ReservedInsets(0, 0, 0, 458);

        var plan = WorkAreaCalculator.Plan(Monitor, new MarginSet(196, 213, 37, 410), reserved, TaskbarHeight);

        Assert.DoesNotContain(plan.Strips, s => s.Side == MarginSide.Bottom);
        Assert.Equal(new PixelRect(196, 37, 1511, 585), plan.ExpectedWorkArea);
    }

    [Fact]
    public void Plan_CompensatesEachReservedSideIndependently()
    {
        // סרגל יישום של תוכנית אחרת תופס 60 פיקסלים בצד שמאל.
        var existing = new ReservedInsets(60, 0, 0, 0);

        var plan = WorkAreaCalculator.Plan(Monitor, new MarginSet(100, 0, 0, 0), existing);

        var left = plan.Strips.Single(s => s.Side == MarginSide.Left);
        Assert.Equal(new PixelRect(60, 0, 40, 1080), left.Rect);
        Assert.Equal(100, plan.ExpectedWorkArea.X);
        Assert.True(plan.IsExact);
    }

    [Fact]
    public void Plan_KeepsEveryStripOutOfTheVisibleArea()
    {
        var margins = new MarginSet(196, 213, 37, 410);
        var plan = WorkAreaCalculator.Plan(Monitor, margins, Taskbar);

        foreach (var (_, rect) in plan.Strips)
        {
            var overlap = rect.Intersect(plan.Visible);
            Assert.True(overlap.IsEmpty, $"הסרגל {rect} חורג אל תוך האזור הגלוי {plan.Visible}");
        }
    }

    [Fact]
    public void Plan_OnSecondaryMonitor_KeepsTheDesktopOffset()
    {
        var secondary = new PixelRect(1920, 0, 1920, 1080);
        var margins = new MarginSet(10, 20, 30, 40);

        var plan = WorkAreaCalculator.Plan(secondary, margins, ReservedInsets.None);

        Assert.Equal(new PixelRect(1930, 30, 1890, 1010), plan.ExpectedWorkArea);
    }

    [Fact]
    public void Between_MeasuresWhatTheSystemAlreadyReserved()
    {
        var workArea = new PixelRect(0, 0, 1920, 1032);

        Assert.Equal(new ReservedInsets(0, 0, 0, 48), ReservedInsets.Between(Monitor, workArea));
    }

    [Fact]
    public void Between_IgnoresAWorkAreaLargerThanTheMonitor()
    {
        Assert.True(ReservedInsets.Between(Monitor, new PixelRect(-10, -10, 2000, 1200)).IsZero);
    }

    [Fact]
    public void Matches_DetectsAWorkAreaThatIsNotThePlannedOne()
    {
        var plan = WorkAreaCalculator.Plan(Monitor, new MarginSet(196, 213, 37, 410), Taskbar);

        Assert.True(WorkAreaCalculator.Matches(plan, plan.ExpectedWorkArea));

        // אזור עבודה קצר מהמתוכנן בסרגל המשימות — בדיוק התקלה שהתגלתה בשטח.
        var shortByTaskbar = new PixelRect(196, 37, 1511, 585);
        Assert.False(WorkAreaCalculator.Matches(plan, shortByTaskbar));
        Assert.True(WorkAreaCalculator.StaysInside(plan, shortByTaskbar));
    }

    [Fact]
    public void StaysInside_RejectsAWorkAreaThatReachesIntoTheHiddenMargin()
    {
        var plan = WorkAreaCalculator.Plan(Monitor, new MarginSet(196, 213, 37, 410), Taskbar);

        Assert.False(WorkAreaCalculator.StaysInside(plan, new PixelRect(196, 37, 1511, 700)));
        Assert.False(WorkAreaCalculator.StaysInside(plan, new PixelRect(100, 37, 1511, 633)));
    }

    [Fact]
    public void Max_UsesTheLargerReservationPerSide()
    {
        var existing = new ReservedInsets(60, 0, 0, 48);

        var combined = existing.Max(new ReservedInsets(196, 37, 213, 20));

        Assert.Equal(new ReservedInsets(196, 37, 213, 48), combined);
    }
}
