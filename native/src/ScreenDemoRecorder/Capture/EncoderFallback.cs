namespace ScreenDemoRecorder.Capture;

internal sealed record EncoderFallbackResult<T>(T Value, bool UsedSoftware);

internal sealed class EncoderPreparationException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

internal static class EncoderFallback
{
    public static async Task<EncoderFallbackResult<T>> PrepareAsync<T>(Func<bool, Task<T>> attempt, Func<T, string?> problem)
    {
        string? hardwareProblem;
        Exception? hardwareError = null;
        try
        {
            var hardware = await attempt(true);
            hardwareProblem = problem(hardware);
            if (hardwareProblem is null) return new EncoderFallbackResult<T>(hardware, false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            hardwareError = error;
            hardwareProblem = error.Message;
        }

        string? softwareProblem;
        Exception? softwareError = null;
        try
        {
            var software = await attempt(false);
            softwareProblem = problem(software);
            if (softwareProblem is null) return new EncoderFallbackResult<T>(software, true);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            softwareError = error;
            softwareProblem = error.Message;
        }

        throw new EncoderPreparationException(
            $"The H.264 encoder could not start. Hardware: {hardwareProblem}. Software: {softwareProblem}.",
            softwareError ?? hardwareError);
    }
}
