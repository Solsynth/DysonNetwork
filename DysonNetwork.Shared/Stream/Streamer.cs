using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace DysonNetwork.Shared.Stream;

public static class Streamer
{
    public static async Task<INatsJSStream> EnsureStreamCreated(
        this INatsJSContext context,
        string stream,
        ICollection<string>? subjects
    )
    {
        try
        {
            return await context.CreateStreamAsync(new StreamConfig(stream, subjects ?? []));
        }
        catch (NatsJSException)
        {
            return await context.GetStreamAsync(stream);
        }
    }
}