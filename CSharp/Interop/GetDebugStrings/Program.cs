using System;

namespace GetDebugStrings
{
    // based on https://www.codeproject.com/Articles/13345/DbMon-NET-A-simple-NET-OutputDebugString-capturer
    // more background on OutputDebugString: http://www.unixwiz.net/techtips/outputdebugstring.html
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
