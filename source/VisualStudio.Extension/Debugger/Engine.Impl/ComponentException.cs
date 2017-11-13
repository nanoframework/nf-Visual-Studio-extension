using System;
using System.Runtime.Serialization;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    [Serializable]
    internal class ComponentException : Exception
    {
        public ComponentException(int hr)
        {
            HResult = hr;
        }
    }
}