//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.Extensions;
using nanoFramework.Tools.Debugger.WireProtocol;
using Task = System.Threading.Tasks.Task;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// Shows the details and capabilities of a device in the Visual Studio Properties window.
    /// </summary>
    internal static class DeviceProperties
    {
        private const string DeviceCategory = "Device";
        private const string FirmwareCategory = "Firmware";
        private const string BooterCategory = "Booter";
        private const string CapabilitiesCategory = "Capabilities";
        private const string MemoryCategory = "Memory";
        private const string AssembliesCategory = "Native assemblies";

        private const string DeviceExplorerCaption = "Device Explorer";

        // the Device Explorer of the previous versions of the extension, can still be in a saved window layout
        private static readonly Guid s_legacyDeviceExplorerGuid = new Guid("CCD64499-4029-4B29-9518-00E9B10C876E");

        private static readonly Guid s_propertiesWindowGuid = new Guid(ToolWindowGuids80.PropertiesWindow);

        /// <summary>
        /// Makes <paramref name="device"/> the selection of the Device Explorer tool window, so the Properties window shows it.
        /// </summary>
        /// <param name="device">The device, <see langword="null"/> to clear the selection.</param>
        /// <param name="showPropertiesWindow"><see langword="true"/> to also show the Properties window.</param>
        public static async Task PublishAsync(NanoDeviceBase device, bool showPropertiesWindow = false)
        {
            // read the device before switching to the main thread
            var properties = device is null ? null : Create(device);

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var deviceExplorerFrame = GetDeviceExplorerFrame();

                // the Properties window shows the selection of the active window: make sure that's Device Explorer when asked for its properties
                if (showPropertiesWindow)
                {
                    deviceExplorerFrame?.Show();
                }

                var trackSelection = GetTrackSelection(deviceExplorerFrame);

                if (trackSelection != null)
                {
                    var selection = new SelectionContainer(true, false);

                    if (properties != null)
                    {
                        selection.SelectableObjects = new object[] { properties };
                        selection.SelectedObjects = new object[] { properties };
                    }

                    trackSelection.OnSelectChange(selection);
                }

                if (showPropertiesWindow
                    && await AsyncServiceProvider.GlobalProvider.GetServiceAsync(typeof(SVsUIShell)) is IVsUIShell shell)
                {
                    var propertiesWindow = s_propertiesWindowGuid;

                    if (shell.FindToolWindow((uint)__VSFINDTOOLWIN.FTW_fForceCreate, ref propertiesWindow, out IVsWindowFrame frame) == 0
                        && frame != null)
                    {
                        frame.ShowNoActivate();

                        DockUnderDeviceExplorer(frame, deviceExplorerFrame);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageCentre.InternalErrorWriteLine($"Error showing the device properties: {ex.Message}");
            }
        }

        /// <summary>
        /// Builds the object shown in the Properties window for <paramref name="device"/>.
        /// </summary>
        public static PropertyBag Create(NanoDeviceBase device)
        {
            var properties = new PropertyBag(device.Description, "nanoDevice");

            try
            {
                properties
                    .Add(DeviceCategory, "Name", device.Description)
                    .Add(DeviceCategory, "Target", device.TargetName)
                    .Add(DeviceCategory, "Platform", device.Platform)
                    .Add(DeviceCategory, "Transport", device.Transport.ToString())
                    .Add(DeviceCategory, "Port", device.ConnectionId)
                    .Add(DeviceCategory, "Serial number", device.SerialNumber);

                var deviceInfo = device.DeviceInfo is { Valid: true } info ? info : null;

                properties
                    .Add(FirmwareCategory, "CLR version", VersionOrNull(device.CLRVersion), "Version of nanoCLR running on the device.")
                    .Add(FirmwareCategory, "Firmware version", VersionOrNull(deviceInfo?.SolutionBuildVersion))
                    .Add(FirmwareCategory, "Build info", deviceInfo?.SolutionBuildInfo)
                    .Add(FirmwareCategory, "HAL version", VersionOrNull(deviceInfo?.HalBuildVersion))
                    .Add(FirmwareCategory, "HAL build info", deviceInfo?.HalBuildInfo)
                    .Add(FirmwareCategory, "Target framework", VersionOrNull(deviceInfo?.TargetFrameworkVersion));

                properties
                    .Add(BooterCategory, "nanoBooter", device.HasNanoBooter ? VersionOrNull(device.BooterVersion) ?? "Yes" : "No")
                    .Add(BooterCategory, "Proprietary bootloader", device.HasProprietaryBooter ? "Yes" : "No")
                    .Add(BooterCategory, "MCUboot", device.HasMCUboot ? "Yes" : "No");

                AddCapabilities(properties, DeviceOperations.GetCachedCapabilitiesData(device));
            }
            catch (Exception ex)
            {
                // the device can be gone while reading its properties
                MessageCentre.InternalErrorWriteLine($"Error reading the properties of {device.Description}: {ex.Message}");
            }

            return properties;
        }

        private static void AddCapabilities(PropertyBag properties, DeviceCapabilitiesData data)
        {
            if (data is null)
            {
                properties.Add(CapabilitiesCategory, "State", "Not read", "The device couldn't be read yet. Use Device capabilities in Device Explorer to read it.");
                return;
            }

            properties.Add(CapabilitiesCategory, "Running", data.ConnectionSource.ToString(), "What was running on the device when it was queried.");

            if (data.ConnectionSource == ConnectionSource.nanoBooter)
            {
                properties.Add(CapabilitiesCategory, "Target info", data.TargetInfo);
                return;
            }

            if (data.Capabilities is { IsUnknown: false } capabilities)
            {
                properties
                    .Add(CapabilitiesCategory, "Floating point", YesNo(capabilities.FloatingPoint))
                    .Add(CapabilitiesCategory, "Source level debugging", YesNo(capabilities.SourceLevelDebugging))
                    .Add(CapabilitiesCategory, "App domains", YesNo(capabilities.AppDomains))
                    .Add(CapabilitiesCategory, "Exception filters", YesNo(capabilities.ExceptionFilters))
                    .Add(CapabilitiesCategory, "Incremental deployment", YesNo(capabilities.IncrementalDeployment))
                    .Add(CapabilitiesCategory, "Soft reboot", YesNo(capabilities.SoftReboot))
                    .Add(CapabilitiesCategory, "Profiling", YesNo(capabilities.Profiling))
                    .Add(CapabilitiesCategory, "Can change MAC address", YesNo(capabilities.CanChangeMacAddress))
                    .Add(CapabilitiesCategory, "Config block requires erase", YesNo(capabilities.ConfigBlockRequiresErase));
            }

            if (data.MemoryMap?.Count > 0)
            {
                var map = new PropertyBag($"{data.MemoryMap.Count} regions");

                foreach (var range in data.MemoryMap)
                {
                    string type = (range.m_flags & Commands.Monitor_MemoryMap.c_RAM) != 0 ? "RAM" : (range.m_flags & Commands.Monitor_MemoryMap.c_FLASH) != 0 ? "FLASH" : "Other";

                    map.Add(null, $"{type} 0x{range.m_address:X8}", FormatSize(range.m_length));
                }

                properties.Add(MemoryCategory, "Memory map", map);
            }

            if (data.FlashSectorMap?.Count > 0)
            {
                var map = new PropertyBag($"{data.FlashSectorMap.Count} regions");

                foreach (var sector in data.FlashSectorMap)
                {
                    map.Add(
                        null,
                        $"0x{sector.StartAddress:X8}",
                        $"{sector.UsageAsString(false).Trim()}, {sector.NumBlocks} x {FormatSize(sector.BytesPerBlock)} ({FormatSize(sector.NumBlocks * sector.BytesPerBlock)})");
                }

                properties.Add(MemoryCategory, "Flash sector map", map);
            }

            if (data.DeploymentMap != null)
            {
                var map = new PropertyBag($"{data.DeploymentMap.Count} assemblies");

                for (int i = 0; i < data.DeploymentMap.Count; i++)
                {
                    var assembly = data.DeploymentMap[i];

                    map.Add(null, $"[{i}] 0x{assembly.m_address:X8}", $"{FormatSize(assembly.m_size)}, CRC 0x{assembly.m_CRC:X8}");
                }

                properties.Add(MemoryCategory, "Deployment map", map);
            }

            var nativeAssemblies = data.DeviceInfo?.NativeAssemblies;

            if (nativeAssemblies?.Count > 0)
            {
                foreach (var assembly in nativeAssemblies.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
                {
                    // nanoCLR v2 and later report the native contract hash instead of a version
                    properties.Add(
                        AssembliesCategory,
                        assembly.Name,
                        assembly.Version is null ? $"0x{assembly.Checksum:X8}" : $"v{assembly.Version} 0x{assembly.Checksum:X8}");
                }
            }
        }

        private static string VersionOrNull(Version version) =>
            version is null || version == new Version(0, 0) || version == new Version(0, 0, 0, 0) ? null : version.ToString();

        private static string YesNo(bool value) => value ? "Yes" : "No";

        private static string FormatSize(uint bytes) =>
            bytes >= 1024 * 1024 && bytes % (1024 * 1024) == 0 ? $"{bytes / (1024 * 1024)} MB"
            : bytes >= 1024 && bytes % 1024 == 0 ? $"{bytes / 1024} kB"
            : $"{bytes} bytes";

        /// <summary>
        /// Docks the Properties window under Device Explorer, when it would otherwise be a tab in the same group and hide it.
        /// </summary>
        /// <remarks>
        /// Visual Studio has no public API to dock a tool window next to another one: this uses the window manager's own
        /// <c>DockOperations</c> through reflection, and leaves the window where it is when that isn't available.
        /// </remarks>
        private static void DockUnderDeviceExplorer(IVsWindowFrame propertiesFrame, IVsWindowFrame deviceExplorerFrame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (deviceExplorerFrame is null)
                {
                    return;
                }

                object propertiesView = GetFrameView(propertiesFrame);
                object deviceExplorerView = GetFrameView(deviceExplorerFrame);

                if (propertiesView is null || deviceExplorerView is null)
                {
                    return;
                }

                var parentProperty = propertiesView.GetType().GetProperty("Parent");

                // a Properties window placed somewhere else by the user stays there
                bool sharesGroup = Equals(parentProperty?.GetValue(propertiesView), parentProperty?.GetValue(deviceExplorerView));

                if (!sharesGroup)
                {
                    return;
                }

                // the views are ToolWindowView (window management assembly), their base View is in the view manager assembly with DockOperations
                Type dockOperations = null;
                Type dockDirection = null;

                for (var type = propertiesView.GetType(); type != null && dockOperations is null; type = type.BaseType)
                {
                    dockOperations = type.Assembly.GetType("Microsoft.VisualStudio.PlatformUI.Shell.DockOperations");
                    dockDirection = type.Assembly.GetType("Microsoft.VisualStudio.PlatformUI.Shell.DockDirection");
                }

                var dock = dockOperations?.GetMethods().FirstOrDefault(m => m.Name == "Dock" && m.GetParameters().Length == 3);

                if (dock is null || dockDirection is null)
                {
                    MessageCentre.InternalErrorWriteLine("Couldn't dock the Properties window under Device Explorer: DockOperations not found.");
                    return;
                }

                dock.Invoke(null, new[] { deviceExplorerView, propertiesView, Enum.Parse(dockDirection, "Bottom") });

                propertiesFrame.ShowNoActivate();
            }
            catch (Exception ex)
            {
                // the window just stays where Visual Studio put it
                MessageCentre.InternalErrorWriteLine($"Couldn't dock the Properties window under Device Explorer: {ex.Message}");
            }
        }

        private static object GetFrameView(IVsWindowFrame frame) =>
            frame.GetType().GetProperty("FrameView")?.GetValue(frame);

        // the selection of the Device Explorer tool window frame, not kept: the frame can be replaced when the window is closed and opened again
        private static ITrackSelection GetTrackSelection(IVsWindowFrame frame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (frame != null
                && frame.GetProperty((int)__VSFPROPID.VSFPROPID_SPFrame, out object frameServices) == 0
                && frameServices is Microsoft.VisualStudio.OLE.Interop.IServiceProvider oleServiceProvider)
            {
                return new ServiceProvider(oleServiceProvider).GetService(typeof(STrackSelection)) as ITrackSelection;
            }

            return null;
        }

        // the frame of the Device Explorer tool window, found by its caption: the frames of new model tool windows have generated identifiers
        private static IVsWindowFrame GetDeviceExplorerFrame()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (Package.GetGlobalService(typeof(SVsUIShell)) is not IVsUIShell shell
                || shell.GetToolWindowEnum(out IEnumWindowFrames frames) != 0)
            {
                return null;
            }

            var frame = new IVsWindowFrame[1];

            while (frames.Next(1, frame, out uint fetched) == 0 && fetched == 1)
            {
                if (frame[0].GetProperty((int)__VSFPROPID.VSFPROPID_Caption, out object caption) != 0
                    || caption as string != DeviceExplorerCaption
                    || (frame[0].GetGuidProperty((int)__VSFPROPID.VSFPROPID_GuidPersistenceSlot, out Guid slot) == 0 && slot == s_legacyDeviceExplorerGuid))
                {
                    continue;
                }

                return frame[0];
            }

            return null;
        }
    }

    /// <summary>
    /// A read-only object for the Properties window, with properties defined at run time.
    /// </summary>
    [TypeConverter(typeof(ExpandableObjectConverter))]
    internal sealed class PropertyBag : ICustomTypeDescriptor
    {
        private readonly List<PropertyDescriptor> _properties = new List<PropertyDescriptor>();
        private readonly string _componentName;
        private readonly string _className;

        public PropertyBag(string componentName, string className = null)
        {
            _componentName = componentName;
            _className = className;
        }

        /// <summary>
        /// Adds a property, unless <paramref name="value"/> is <see langword="null"/> or empty.
        /// </summary>
        public PropertyBag Add(string category, string name, object value, string description = null)
        {
            if (value is null || (value is string text && text.Length == 0))
            {
                return this;
            }

            var attributes = new List<Attribute> { ReadOnlyAttribute.Yes };

            if (category != null)
            {
                attributes.Add(new CategoryAttribute(category));
            }

            if (description != null)
            {
                attributes.Add(new DescriptionAttribute(description));
            }

            _properties.Add(new ValueDescriptor(name, value, attributes.ToArray()));

            return this;
        }

        // shown as the value of the collapsed property
        public override string ToString() => _componentName;

        public AttributeCollection GetAttributes() => TypeDescriptor.GetAttributes(this, true);

        public string GetClassName() => _className;

        public string GetComponentName() => _componentName;

        public TypeConverter GetConverter() => new ExpandableObjectConverter();

        public EventDescriptor GetDefaultEvent() => null;

        public PropertyDescriptor GetDefaultProperty() => null;

        public object GetEditor(Type editorBaseType) => null;

        public EventDescriptorCollection GetEvents() => EventDescriptorCollection.Empty;

        public EventDescriptorCollection GetEvents(Attribute[] attributes) => EventDescriptorCollection.Empty;

        public PropertyDescriptorCollection GetProperties() => new PropertyDescriptorCollection(_properties.ToArray(), true);

        public PropertyDescriptorCollection GetProperties(Attribute[] attributes) => GetProperties();

        public object GetPropertyOwner(PropertyDescriptor pd) => this;

        private sealed class ValueDescriptor : PropertyDescriptor
        {
            private readonly object _value;

            public ValueDescriptor(string name, object value, Attribute[] attributes)
                : base(name, attributes)
            {
                _value = value;
            }

            public override Type ComponentType => typeof(PropertyBag);

            public override bool IsReadOnly => true;

            public override Type PropertyType => _value.GetType();

            public override bool CanResetValue(object component) => false;

            public override object GetValue(object component) => _value;

            public override void ResetValue(object component)
            {
            }

            public override void SetValue(object component, object value)
            {
            }

            public override bool ShouldSerializeValue(object component) => false;
        }
    }
}
