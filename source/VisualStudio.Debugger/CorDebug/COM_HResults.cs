namespace nanoFramework.Tools.VisualStudio.Debugger
{
    public class COM_HResults
    {
        public const int S_OK = 0x00000000;
        public const int S_FALSE = 0x00000001;
        public const int CONNECT_E_NOCONNECTION = unchecked((int)0x80040200);
        public const int E_FAIL = unchecked((int)0x80004005);
        public const int E_NOTIMPL = unchecked((int)0x80004001);
        
        //from msdbg.idl
        public const int E_PROCESS_DESTROYED = unchecked((int)0x80040070);

        public static int BOOL_TO_HRESULT(bool b, int hrFalse) { return b ? S_OK : hrFalse; }
        public static int BOOL_TO_HRESULT_FAIL(bool b) { return BOOL_TO_HRESULT(b, E_FAIL); }
        public static int BOOL_TO_HRESULT_FALSE(bool b) { return BOOL_TO_HRESULT(b, S_FALSE); }
    }
}
