namespace BatchRunner.Models
{
    public record AppTask(
        string Path, 
        string Arguments, 
        int? Interval
    );
}
