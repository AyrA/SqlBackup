namespace SqlBackup
{
    internal static class DefaultConnectionStrings
    {
        public const string SqlExpress = @"Server=.\SQLEXPRESS;Trusted_Connection=True;Encrypt=False";
        public const string LocalDb = @"Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;Encrypt=False";
    }
}
