using AI_FGF;

namespace AI_FGF.TestRunner;

internal static class Program
{
    [STAThread]
    private static void Main() => AiFgfApp.Run(() => new MainForm());
}
