using AI_FGF;

namespace AI_FGF.Showcase;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // the showcase opens with the magic glow; the user's pick in the window changes it live
        AiFgfEffects.DefaultButtons = AiFgfButtonStyle.Glow;
        AiFgfApp.Run(() => new ShowcaseForm());
    }
}
