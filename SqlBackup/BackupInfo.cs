namespace SqlBackup
{
    public record BackupInfo(string DatabaseName, DateTime BackupStart, DateTime BackupEnd, long Size, string FileName, BackupType BackupType, DbRecoveryModel RecoveryModel, int FileId);
}
