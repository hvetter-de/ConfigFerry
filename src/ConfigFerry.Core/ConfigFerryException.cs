namespace ConfigFerry.Core;

/// <summary>An expected, user-presentable failure (bad input file, missing permissions, ...).</summary>
public sealed class ConfigFerryException : Exception
{
    public ConfigFerryException()
    {
    }

    public ConfigFerryException(string message)
        : base(message)
    {
    }

    public ConfigFerryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
