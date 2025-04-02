# BatchRunner
## Runs multiple executables at specified intervals, logs output, and optionally executes SQL commands after each run.
Useful in emergency or fallback scenarios where you need to manually run multiple executables & sql commands in sequence in a sequenced timeinterval

- Supports running multiple .exe files in parallel
- Custom interval between runs (per executable)
- Logs any console outputs from .exe's in .log file
- Ability to chain SQL commands

### Example use

```bash
BatchRunner.exe FADTAPIToDW.exe -Interval5 -ConnectionString "Server=.;Database=Test;Trusted_Connection=True;" -RunSqlCommand "DELETE * FROM dbo.FADT" -RunSqlCommand "EXEC dbo.initFADT"
