namespace SystemUptimeTracker.Common.Helpers.Exceptions;

/// <summary>
/// Represents errors that occur during application execution.
/// </summary>
public class ProblemException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProblemException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public ProblemException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProblemException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="ex">The exception that is the cause of the current exception, or a null reference if no inner exception is specified.</param>
    public ProblemException(string message, Exception ex) : base(message, ex)
    {
    }
}