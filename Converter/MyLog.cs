using System;

namespace Converter
{
    public static class MyLog
    {
        public static void Die(string message)
        {
            Console.WriteLine(message);
            Environment.Exit(1);
        }
    }
}