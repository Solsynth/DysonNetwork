namespace DysonNetwork.Sphere.Reader;

public class WebReaderException : Exception
{
    public WebReaderException(string message) : base(message)
    {
    }

    public WebReaderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
