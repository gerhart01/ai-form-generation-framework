using AI_FGF;

namespace AI_FGF.Demo;

internal static class Program
{
    [STAThread]
    static void Main() => AiFgfApp.Run(() => new PingToolForm(), AiFgfTheme.Light);
}
