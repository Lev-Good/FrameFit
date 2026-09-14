using System;
using System.IO;
using System.Runtime.InteropServices;

namespace FrameFit.App.Infrastructure;

/// <summary>
/// אפליקציית WPF אינה מקצה חלון קונסולה. במצב בדיקה עצמית אנו מצטרפים לקונסולה של
/// התהליך שקרא לנו, כדי שאפשר יהיה להריץ את הבדיקה מהטרמינל ולראות פלט.
/// </summary>
internal static class ConsoleBridge
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    public static void AttachToParent()
    {
        try
        {
            AttachConsole(AttachParentProcess);
            var standardOutput = Console.OpenStandardOutput();
            var writer = new StreamWriter(standardOutput) { AutoFlush = true };
            Console.SetOut(writer);

            var standardError = Console.OpenStandardError();
            Console.SetError(new StreamWriter(standardError) { AutoFlush = true });
        }
        catch (Exception)
        {
            // אם אין קונסולה — הבדיקה פשוט לא תדפיס.
        }
    }
}
