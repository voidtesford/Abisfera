using Avalonia;

namespace Abisfera.AnimEditor;

public static class Program
{
    public static string? RepoArg;
    public static string? SceneArg;

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0].StartsWith("--", StringComparison.Ordinal) && args[0] is not ("--repo" or "--scene"))
            return Cli.Run(args);

        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--repo")
                RepoArg = args[i + 1];
            else if (args[i] == "--scene")
                SceneArg = args[i + 1];
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog(e.ExceptionObject as Exception);
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception e)
        {
            CrashLog(e);
            throw;
        }

        return 0;
    }

    public static void CrashLog(Exception? e)
    {
        try
        {
            Directory.CreateDirectory(Settings.Dir);
            File.AppendAllText(Path.Combine(Settings.Dir, "crash.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e}\n\n");
        }
        catch
        {
            // nowhere to write
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
