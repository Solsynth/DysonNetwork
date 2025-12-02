namespace DysonNetwork.Shared.Cache;

public interface ICacheSerializer
{
    string Serialize<T>(T value);
    T? Deserialize<T>(string data);
}