namespace Ferret.Abstractions.Models;

public sealed class InvalidCursorException : Exception
{
    public InvalidCursorException(string message) : base(message) { }
}
