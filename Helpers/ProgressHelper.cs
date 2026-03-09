namespace GraphRagCli.Helpers;

public static class ProgressHelper
{
    public static void WriteProgress(int current, int total, TimeSpan elapsed, int barWidth, string currentItem)
    {
        var pct = (double)current / total;
        var filled = (int)(pct * barWidth);
        var bar = new string('#', filled) + new string('-', barWidth - filled);

        var rate = current / elapsed.TotalSeconds;
        var eta = rate > 0 ? TimeSpan.FromSeconds((total - current) / rate) : TimeSpan.Zero;

        // Truncate item name to fit
        var name = currentItem;
        if (name.Length > 50) name = "..." + name[^47..];

        var line = $"\r  [{bar}] {current}/{total} ({pct:P0}) | {elapsed:mm\\:ss} elapsed | ETA {eta:mm\\:ss} | {name}";

        // Pad to clear previous line, clamp to console width
        try
        {
            var width = Console.WindowWidth;
            if (line.Length > width) line = line[..width];
            else line = line.PadRight(width);
        }
        catch { /* non-interactive console */ }

        Console.Write(line);
    }
}
