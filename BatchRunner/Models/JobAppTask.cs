namespace BatchRunner.Models;

public record JobAppTask(
    string Path,
    string Arguments,
    int? Interval,
    List<SqlStep> Steps
) : AppTask(Path, Arguments, Interval);
