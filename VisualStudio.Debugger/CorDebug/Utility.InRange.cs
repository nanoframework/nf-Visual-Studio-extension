namespace nanoFramework.Tools.VisualStudio.Debugger
{
    public partial class Utility
    {
        public static bool InRange(int i, int iLow, int iHigh)
        {
            return i >= iLow && i <= iHigh;
        }

        public static bool InRange(uint i, uint iLow, uint iHigh)
        {
            return i >= iLow && i <= iHigh;
        }
    }
}
