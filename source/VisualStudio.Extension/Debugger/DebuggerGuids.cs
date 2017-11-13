using System;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    static public class DebuggerGuids
    {
        /// <summary>
        /// This is the engine GUID of the engine. It needs to be changed here and in EngineRegistration.pkgdef
        /// when turning the sample into a real engine.
        /// </summary>
        public static readonly Guid EngineId = new Guid("{1C27E13E-39FE-4FD8-8348-B576D83C58B3}");
        public const string EngineIdAsString = "1C27E13E-39FE-4FD8-8348-B576D83C58B3";

        public static readonly Guid EngineCLSID = new Guid("265E4794-95C9-459D-9555-630A4ADC4BD0");
        public const string EngineCLSIDAsString = "1C27E13E-39FE-4FD8-8348-B576D83C58B3";

        public static readonly Guid ProgramProviderCLSID = new Guid("{A098C157-CB4C-4C35-B3E8-1D86B6CE191E}");
        public const string ProgramProviderCLSIDAsString = "A098C157-CB4C-4C35-B3E8-1D86B6CE191E";

        public static readonly Guid MicrosoftVendorGuid = new Guid("{994B45C4-E6E9-11D2-903F-00C04FA302A1}");
        public const string MicrosoftVendorGuidAsString = "994B45C4-E6E9-11D2-903F-00C04FA302A1";

        public static readonly Guid NanoLanguageGuid = new Guid("{64C17C3A-232B-4B67-B7E3-626B90049575}");
        public const string NanoLanguageGuidAsString = "64C17C3A-232B-4B67-B7E3-626B90049575";

        public static readonly Guid NanoDebugPortSupplierCLSID = new Guid("{274CCEB4-5F00-419A-80B1-3C15A70B8D77}");
        public const string NanoDebugPortSupplierCLSIDAsString = "274CCEB4-5F00-419A-80B1-3C15A70B8D77";

        public static readonly Guid NanoDebugPortSupplierID = new Guid("{69FA8E4E-D542-415A-9756-CA4A00932B9E}");
        public const string NanoDebugPortSupplierIDAsString = "69FA8E4E-D542-415A-9756-CA4A00932B9E";

    }
}
