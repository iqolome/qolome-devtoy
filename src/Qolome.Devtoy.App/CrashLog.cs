namespace Qolome_Devtoy_App;

using Qolome.Devtoy.Core.Services;

internal static class CrashLog
{
    private static readonly string Path = System.IO.Path.Combine(
        DevtoyPaths.AppDataRoot,
        "crash.log");

    public static void Write(string message, Exception? ex = null)
    {
        try
        {
            System.IO.Directory.CreateDirectory(DevtoyPaths.AppDataRoot);
            var text = $"[{DateTimeOffset.Now:O}] {message}";
            if (ex is not null)
            {
                text += Environment.NewLine + ex;
            }

            text += Environment.NewLine;
            System.IO.File.AppendAllText(Path, text);
        }
        catch
        {
            // ignored
        }
    }
}