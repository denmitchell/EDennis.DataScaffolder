This form generates a C# class called "DataFactory.cs", 
which holds static methods that return data that
reside in tables in one ore more databases.  The form 
uses an appsettings.json file from a .NET Core project
to identify one or more connection strings through 
which data are pulled.  The appsettings.json file must
have a top-level property called "ConnectionStrings"
with a second-level property for each connection string.
The DataFactory.cs file is saved in a Models folder
(when it exists) or the same directory as the 
appsettings.json file.

NOTE: __EFMigrationsHistory will be ignored
NOTE: schemas like '%_history' and '_maintenance' will be ignored
NOTE: views will be ignored
NOTE: columns like 'sysstart%' and 'sysend%' will be ignored

