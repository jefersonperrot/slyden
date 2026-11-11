namespace SlideBeat.Core.Audio;

/// <summary>Acha o executável do ffmpeg: variável SLIDEBEAT_FFMPEG, ao lado do programa ou no PATH.</summary>
public static class FfmpegLocator
{
    public const string EnvVar = "SLIDEBEAT_FFMPEG";

    public static string Find()
    {
        string? fromEnv = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv)) return fromEnv;

        string exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        string beside = Path.Combine(AppContext.BaseDirectory, exe);
        if (File.Exists(beside)) return beside;

        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            string candidate = Path.Combine(dir.Trim('"'), exe);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(
            $"ffmpeg não encontrado. Coloque o {exe} ao lado do programa, no PATH, ou aponte {EnvVar}.");
    }
}
