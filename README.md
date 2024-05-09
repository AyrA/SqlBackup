# Sql Backup Utility

This is a simple program that can back up and restore databases of a Microsoft SQL server.

It exclusively uses internal functionality of your SQL server,
and performs proper backups that will truncate the transaction log.

## Simple Backup and Restore Commands

Backing up all databases into individual files:

    SqlBackup.exe /BACKUP /ALL /DIR D:\Backup

Backing up all databases into the same file:

    SqlBackup.exe /BACKUP /ALL /FILE D:\Backup\sqlserver.bak

Restoring all databases from a backup made up of individual files:

    SqlBackup.exe /RESTORE /ALL /DIR D:\Backup

Restoring all databases from one file:

    SqlBackup.exe /RESTORE /ALL /FILE D:\Backup\sqlserver.bak

## Scheduled Backup

Use the task scheduler (Run: `taskschd.msc`) to configure a scheduled backup.

## Advanced Usage

Use the `/?` argument to get detailed usage instructions.

## Common Arguments

### `/C <str>`

This specifies the connection string, and is required for all modes except the help.
You can specify "EXPRESS" or "LOCAL" to get predefined strings for a local SQL Server Express instance,
or the LocalDB instance.

### `/ALL [db ...]`

Specifies to operate on all databases, optionally excluding databases specified afterwards.
Mutually exclusive with `/DB`

### `/DB db [...]`

Specifies to operate on the specified databases,
at least one must be specified.
This is necessary to restore a deleted database,
because otherwise the server will not know the name.
Mutually exclusive with `/ALL`

### `/DIR <path>`

Specifies the directory where backup files are read or written to.
The application will use `<dbname>.db.bak` as file name for each database.
Mutually exclusive with `/FILE`

**This path is local to the SQL server and not the backup utility.**

### `/FILE <path>`

Specifies the file where backups are written to or read from.
Multiple databases can be backed up into the same file.
Mutually exclusive with `/DIR`

**This path is local to the SQL server and not the backup utility.**

## Other Commands

Other commands supported by this application include:

- Listing all databases
- Showing database details
- Showing backups from the server backup history
- Showing backups from a backup file
- Taking a database offline or online
- Purging the backup history
- Changing the recovery mode

## More

The file `S3.sql` shows how you can back up your database to an S3 bucket.
