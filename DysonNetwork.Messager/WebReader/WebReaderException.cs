namespace DysonNetwork.Messager.WebReader;

/// <summary>
/// Exception thrown when an error occurs during web reading operations
/// </summary>
public class WebReaderException : Exception
{
    public WebReaderException(string message) : base(message)
    {
    }

    public WebReaderException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
