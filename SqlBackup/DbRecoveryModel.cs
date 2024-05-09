namespace SqlBackup
{
    public enum DbRecoveryModel : byte
    {
        Full = 1,
        BulkLogged = 2,
        Simple = 3
    }
}
