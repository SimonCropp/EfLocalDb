using System;
using System.IO;

static class Guard
{
    public static void AgainstNull(string argumentName, object value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(argumentName);
        }
    }

    public static void DirectoryExists(string argumentName, string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new Exception($"Path to {argumentName} does not exist: {directory}");
        }
    }

    public static void AgainstNullWhiteSpace(string argumentName, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentNullException(argumentName);
        }
    }

    public static void AgainstWhiteSpace(string argumentName, string value)
    {
        if (value == null)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentNullException(argumentName);
        }
    }

    public static void AgainstNegative(string argumentName, int value)
    {
        if (value < 0)
        {
            throw new ArgumentNullException(argumentName);
        }
    }
}