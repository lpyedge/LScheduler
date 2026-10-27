namespace LScheduler;

public static class Guard
{
    public static void NotNull(object? value, string paramName)
    {
        if (value is null)
        {
            throw new ArgumentNullException(paramName);
        }
    }
}
