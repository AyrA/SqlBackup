namespace SqlBackup
{
    public record DbInfo(string DatabaseName, DateTime CreatedAt, AccessType AccessType, DbState State, DbRecoveryModel RecoveryModel, bool IsReadonly);
}
