using System;
using System.Threading;
using System.Diagnostics;

namespace $safeprojectname$
{
    public class Program
    {
        public static void Main()
        {
            Thread.Sleep(1000);  // Give some time to debugger to attach

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
                Thread.Sleep(Timeout.Infinite);
            }
        }
    }
}
