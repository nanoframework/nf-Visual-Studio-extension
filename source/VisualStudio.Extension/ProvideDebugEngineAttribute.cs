
using System;
using System.IO;
using Microsoft.VisualStudio.Shell;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    // this attribute (deriving from RegistrationAttribute) is parsed and the registry entries in the Register method bellow 
    // are loaded into the package .pkgdef file

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    class ProvideDebugEngineAttribute : RegistrationAttribute
    {
        private readonly string _name;
        private readonly Guid _id;
        private readonly bool _setNextStatement, _hitCountBp;
        private readonly Type _debugEngine;

        Guid COMPlusOnlyDebugEngineCLSID = new Guid("{7386310B-D5CB-4369-9BDD-609B3F103914}");

        public ProvideDebugEngineAttribute(string name, Type debugEngine, string id, bool setNextStatement = true, bool hitCountBp = false)
        {
            _name = name;
            _debugEngine = debugEngine;
            _id = new Guid(id);
            _setNextStatement = setNextStatement;
            _hitCountBp = hitCountBp;
        }

        public override void Register(RegistrationContext context)
        {
            var engineKey = context.CreateKey("AD7Metrics\\Engine\\" + _id.ToString("B"));
            engineKey.SetValue("Name", _name);
            engineKey.SetValue("CLSID", COMPlusOnlyDebugEngineCLSID.ToString("B"));

            engineKey.SetValue("PortSupplier", DebugPortSupplier.PortSupplierGuid.ToString("B"));
            engineKey.SetValue("ProgramProvider", COMPlusOnlyDebugEngineCLSID.ToString("B"));  //{708C1ECA-FF48-11D2-904F-00C04FA302A1}

            engineKey.SetValue("Attach", 0);
            engineKey.SetValue("CallstackBP", 1);
            engineKey.SetValue("ConditionalBP", 1);
            engineKey.SetValue("DataBP", 0);
            engineKey.SetValue("Disassembly", 0);
            engineKey.SetValue("Embedded", 0);
            engineKey.SetValue("ENC", 0);
            engineKey.SetValue("EngineCanWatchProcess", 0);
            engineKey.SetValue("EnginePriority", 50);
            engineKey.SetValue("Exceptions", 1);
            engineKey.SetValue("FunctionBP", 1);
            engineKey.SetValue("HitCountBP", _hitCountBp ? 1 : 0);
            engineKey.SetValue("InterceptCurrentException", 0);
            engineKey.SetValue("Interop", 0);
            engineKey.SetValue("JITDebug", 0);
            engineKey.SetValue("NativeInteropOK", 0);
            engineKey.SetValue("Registers", 0);
            engineKey.SetValue("RemoteDebugging", 0);
            engineKey.SetValue("Runtime", typeof(CorDebug).GUID.ToString("B"));  //typeof(CorDebug)
            engineKey.SetValue("SetNextStatement", _setNextStatement ? 1 : 0);
            engineKey.SetValue("SqlCLR", 0);
            engineKey.SetValue("SuspendThread", 1);
            engineKey.SetValue("UseShimAPI", 0);


            //using (var incompatKey = engineKey.CreateSubkey("IncompatibleList"))
            //{
            //    incompatKey.SetValue("guidCOMPlusOnlyEng2", "{5FFF7536-0C87-462D-8FD2-7971D948E6DC}");
            //}

            //using (var autoSelectIncompatKey = engineKey.CreateSubkey("AutoSelectIncompatibleList"))
            //{
            //    autoSelectIncompatKey.SetValue("guidNativeOnlyEng", "{3B476D35-A401-11D2-AAD4-00C04F990171}");
            //}

            var clsidKey = context.CreateKey("CLSID");
            var clsidGuidKey = clsidKey.CreateSubkey(_debugEngine.GUID.ToString("B"));
            clsidGuidKey.SetValue("Class", _debugEngine.FullName);
            clsidGuidKey.SetValue("InprocServer32", context.InprocServerPath);
            clsidGuidKey.SetValue("CodeBase", Path.Combine(context.ComponentPath, _debugEngine.Module.Name));
            clsidGuidKey.SetValue("ThreadingModel", "Both");

            //using (var exceptionAssistantKey = context.CreateKey("ExceptionAssistant\\KnownEngines\\" + _id))
            //{
            //    exceptionAssistantKey.SetValue("", _name);
            //}
        }

        public override void Unregister(RegistrationContext context)
        {
        }
    }
}
