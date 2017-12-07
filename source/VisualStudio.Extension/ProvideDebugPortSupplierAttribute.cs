using System;
using System.IO;
using Microsoft.VisualStudio.Shell;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    // this attribute (deriving from RegistrationAttribute) is parsed and the registry entries in the Register method bellow 
    // are loaded into the package .pkgdef file

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    internal class ProvideDebugPortSupplierAttribute : RegistrationAttribute
    {
        protected string _name;
        protected Guid _id;
        protected Type _portSupplier, _portPicker;

        public ProvideDebugPortSupplierAttribute(string name, Type portSupplier, string id, Type portPicker = null)
        {
            _name = name;
            _portSupplier = portSupplier;
            _id = new Guid(id);
            _portPicker = portPicker;
        }

        public override void Register(RegistrationContext context)
        {
            var portSupplierKey = context.CreateKey("AD7Metrics\\PortSupplier\\" + _id.ToString("B"));
            portSupplierKey.SetValue("Name", _name);
            portSupplierKey.SetValue("CLSID", _portSupplier.GUID.ToString("B"));

            if (_portPicker != null)
            {
                portSupplierKey.SetValue("PortPickerCLSID", _portPicker.GUID.ToString("B"));
            }

            var clsidKey = context.CreateKey("CLSID");
            var clsidGuidKey = clsidKey.CreateSubkey(_portSupplier.GUID.ToString("B"));
            clsidGuidKey.SetValue("Class", _portSupplier.FullName);
            clsidGuidKey.SetValue("InprocServer32", context.InprocServerPath);
            clsidGuidKey.SetValue("CodeBase", Path.Combine(context.ComponentPath, _portSupplier.Module.Name));
            clsidGuidKey.SetValue("ThreadingModel", "Both");
        }

        public override void Unregister(RegistrationContext context)
        {
        }
    }
}

