namespace BatchRunner.Models
{
    public abstract record SqlStep;
    public record RunSqlCommand(string Command) : SqlStep;
}
