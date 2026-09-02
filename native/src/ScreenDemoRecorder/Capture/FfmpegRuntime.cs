namespace ScreenDemoRecorder.Capture;

internal static class FfmpegRuntime
{
    public static string ExecutablePath => Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe");

    public static string RequireExecutable()
    {
        var path = ExecutablePath;
        if (!File.Exists(path))
            throw new FileNotFoundException("The verified FFmpeg runtime is not packaged with the application.", path);
        return path;
    }
}
