/*
	This file demonstrates how to back up an SQL server to an S3 style storage service.
	This is not supported by SqlBackup.exe, but may be a viable alternative.

	You need:
	- A fairly recent SQL server that supports the URL backup target
	- An S3 style blob storage service (for example Amazon S3 or local min.io installation)
	- S3 credentials. These are created in the form of "token_id:token_secret"

	Note: If you opt for a local installation, be sure to serve it over https.
	SQL server will refuse to connect to your service without TLS.
*/

USE [master];

-- ### Registering your S3 access token in SQL server ### --

/*
	Run this command once to create a credential.
	Change the URL to match your setup,
	it should contain everything up to and including the bucket name.
	Do not change to "https", leave the protocol as "s3"
*/
CREATE CREDENTIAL [s3://localhost.ayra.ch:9000/dbbackup]
WITH
-- Do not change the IDENTITY. SQL server searches for this exact string
IDENTITY	= N'S3 Access Key',
-- Supply your S3 token here
SECRET		= N'token_id:token_secret';

-- ### Running a backup into an S3 instance ### --

/*
	Run this backup command for every database
	Note: The backup may fail if the file gets too big.
	In this case, add more "URL=N'...'" lines with unique file names.
	SQL server will split the backup evently across every file.
	Up to 64 URLs can be specified.
	Be aware that each file will be at least 20 MB.

	Note:
	SQL server S3 backup does not support appending to existing files.
	Be sure to move the files away after each backup,
	or change the hardcoded URLs to dynamic URLs with a date+time in them.
	This example command uses "FORMAT", which will overwrite existing files.
*/
BACKUP DATABASE [msdb]
TO
	URL = N's3://localhost.ayra.ch:9000/dbbackup/backup_1.bin'
	-- ,URL = N's3://localhost.ayra.ch:9000/dbbackup/backup_2.bin'
WITH
	COPY_ONLY, -- Remove this line to make the server treat this as a real backup
	FORMAT,    -- Remove this line to abort instead of overwriting your backup
-- Set your S3 bucket region properly or the backup fails
BACKUP_OPTIONS = '{"s3": {"region":"ch-central"}}';

-- ### Removing a credential ### --

/*
	It's generally a good idea to remove the credential if it's no longer needed.
	If you leave the credential on the server for a long time,
	consider setting appropriate access permissions so only the backup user can access it.
*/
DROP CREDENTIAL [s3://localhost.ayra.ch:9000/dbbackup];