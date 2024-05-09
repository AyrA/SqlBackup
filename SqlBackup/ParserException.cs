namespace SqlBackup
{
    public class ParserException : Exception
    {
        public ParserException() : this("Failed to parse command line arguments")
        {
        }

        public ParserException(string? message) : base(message)
        {
        }

        public ParserException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}
