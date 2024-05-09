namespace SqlBackup
{
    public enum ArgumentOpMode
    {
        None,
        Help,
        Invalid,
        ListDb,
        Backup,
        Restore,
        ChangeMode,
        BackupInfo,
        DbInfo,
        PurgeBackup,
        Offline,
        Online
    }
}
