using System;

namespace GetDebugStrings
{
    public class Program
    {
        public static void Main()
        {
            OdsMonitor.Start();
            OdsMonitor.OnOutputDebugString += new OnOutputDebugStringHandler(OnOutputDebugString);

            Console.WriteLine("Press 'Enter' to exit.");
            Console.ReadLine();

            OdsMonitor.Stop();
        }

        private static void OnOutputDebugString(int pid, string text)
        {
            Console.WriteLine(DateTime.Now + ": " + pid + " - " + text);
        }
    }
}
