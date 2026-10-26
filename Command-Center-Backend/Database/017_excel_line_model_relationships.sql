/*
   Deprecated compatibility migration.
   Models are customer/brand-level records. Any active model may run on any
   active production line in that customer's category, so no LineModel rows
   are required for Excel imports or daily reporting.
*/
USE DixonCommandCenterBackup;
GO
