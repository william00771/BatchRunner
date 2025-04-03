# BatchRunner
## Runs multiple executables at specified intervals, logs output, and optionally executes SQL commands after each run.
Useful in emergency or fallback scenarios where you need to manually run multiple executables & sql commands in a sequenced timeinterval

- Supports running multiple .exe files in parallel
- Custom interval between runs (per executable)
- Logs any console outputs from .exe's in .log file
- Ability to chain SQL commands

### Example use
```bash
BatchRunner.exe "..\FADTAPIToDW.exe" -Interval 30
BatchRunner.exe "..\FADTAPIToDW.exe" -Interval 10 "..\APIFetch.exe" -Interval 30 "..\biloload.exe" -Interval 50
```

Execute/chain SQL queries after program execution
```bash
BatchRunner.exe "..\FADTAPIToDW.exe" -Interval 5 -ConnectionString "Server=.;Database=Test;Trusted_Connection=True;" -RunSqlCommand "DELETE * FROM dbo.FADT" -RunSqlCommand "EXEC dbo.initFADT"
```

Change connection string between SQL queries
```bash
BatchRunner.exe "..\FADTAPIToDW.exe" -Interval 30 -ConnectionString "Server=server1;Database=dsz;Trusted_Connection=True;" -RunSqlCommand "DELETE * FROM dbo.FADT" -ConnectionString "Server=server2;Database=lso24;Trusted_Connection=True;" -RunSqlCommand "EXEC dbo.initFADT" -ConnectionString "Server=server3;Database=temp;Trusted_Connection=True;" -RunSqlCommand "exec [dbo].[createTemp]"
```
