using System;
using System.Threading;
using System.Diagnostics;

namespace $safeprojectname$
{
    public class Program
    {
        public static void Main()
        {
            while (!Debugger.IsAttached) { Thread.Sleep(100); }    // Wait for debugger (only needed for debugging session)
            Console.WriteLine("Program started");                  // You can remove this line once it outputs correctly on the console

            try
            {
                // User code goes here
            }
            catch (Exception ex)
            {
                // Do whatever please you with the exception caught
            }
            finally    // Enter the infinite loop in all cases
            {
                while (true)
                {
                    Thread.Sleep(200);
                }
            }
        }
    }
}
