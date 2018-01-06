using System;
using System.Threading;

namespace $safeprojectname$
{
    public class Program
    {
        public static void Main()
        {
          Thread.Sleep(1000);	// Temporary dirty fix to enable correct debugging session
	  // Insert your code below this line

		
          // The main() method has to end with this infinite loop.
          // Do not use the NETMF style : Thread.Sleep(Timeout.Infinite)
	  while (true)
          {
              Thread.Sleep(200);
          }
        }
    }
}
