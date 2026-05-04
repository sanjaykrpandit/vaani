using Lipi.Models;

namespace Lipi.Services;

public interface ILipiRealtimeClient : IDisposable
{
    event Action<string, Dictionary<string, string>>? RecognizingReceived;
    event Action<string, Dictionary<string, string>>? RecognizedReceived;
    event Action<string>? ErrorReceived;
    event Action<bool>? RunningStateChanged;

    Task StartAsync(
        string hubUrl,
        string sessionToken,
        string meetingId,
        string sessionId,
        string sourceLanguage,
        IEnumerable<string> targetLanguages,
        int inputDeviceNumber);

    Task StopAsync();
}