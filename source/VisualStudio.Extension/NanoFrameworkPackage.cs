//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//
using Microsoft.VisualStudio.ProjectSystem.VS;
using nanoFramework.Tools.VisualStudio.Extension;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using nanoFramework.Tools.VisualStudio.Extension.ToolWindow.ViewModel;
using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;

[assembly: ProjectTypeRegistration(projectTypeGuid: NanoFrameworkPackage.ProjectTypeGuid,
                                displayName: "NanoCSharpProject",
                                displayProjectFileExtensions: "#2",
                                defaultProjectExtension: NanoCSharpProjectUnconfigured.ProjectExtension,
                                language: NanoCSharpProjectUnconfigured.Language,
                                resourcePackageGuid: NanoFrameworkPackage.PackageGuid,
                                PossibleProjectExtensions = NanoCSharpProjectUnconfigured.ProjectExtension,
                                Capabilities = NanoCSharpProjectUnconfigured.UniqueCapability
                                )]

namespace nanoFramework.Tools.VisualStudio.Extension
{
    [ProvideAutoLoad(UIContextGuids.NoSolution)]
    [ProvideAutoLoad(UIContextGuids.SolutionExists)]
    [PackageRegistration(AllowsBackgroundLoading = true, RegisterUsing = RegistrationMethod.CodeBase, UseManagedResourcesOnly = true)]
    // info that shown on extension catalog
    [Description("Visual Studio 2017 extension for nanoFramework. Enables creating C# Solutions to be deployed to a target board and provides debugging tools.")]
    // menu for ToolWindow
    [ProvideMenuResource("Menus.ctmenu", 1)]
    // declaration of Device Explorer ToolWindo that (as default) will show tabbed in Solution Explorer
    [ProvideToolWindow(typeof(DeviceExplorer), Style = VsDockStyle.Tabbed, Window = "3ae79031-e1bc-11d0-8f78-00a0c9110057")]
    [Guid(NanoFrameworkPackage.PackageGuid)]
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1650:ElementDocumentationMustBeSpelledCorrectly", Justification = "pkgdef, VS and vsixmanifest are valid VS terms")]
    public sealed class NanoFrameworkPackage : Package
    {
        /// <summary>
        /// The GUID for this project type
        /// </summary>
        public const string ProjectTypeGuid = "11A8DD76-328B-46DF-9F39-F559912D0360";

        /// <summary>
        /// The GUID for this package.
        /// </summary>
        public const string PackageGuid = "046B40EB-1DE1-4D08-AF61-FDB7592B9BBD";


        public const string NanoCSharpProjectSystemCommandSet = "{DF641D51-1E8C-48E4-B549-CC6BCA9BDE19}";


        /// <summary>
        /// View model locator 
        /// </summary>
        static internal ViewModelLocator ViewModelLocator;

        /// <summary>
        /// Path for nanoFramework Extension directoy
        /// </summary>
        public static string NanoFrameworkExtensionDirectory { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="NanoFrameworkPackage"/> class.
        /// </summary>
        public NanoFrameworkPackage()
        {
            // Place any initialization code that does not require
            // any Visual Studio service because at this point the package object is created but
            // not sited yet inside Visual Studio environment. The place to do all the other
            // initialization is the Initialize method.

            // fill the property holding the extension install directory
            var assembly = GetType().Assembly;
            if (assembly.Location == null) throw new Exception("Could not get assembly location!");
            var info = new FileInfo(assembly.Location).Directory;
            if (info == null) throw new Exception("Could not get assembly directory!");
            NanoFrameworkExtensionDirectory = info.FullName;

            // Need to add the View model Locator to the application resource dictionry programatically 
            // becuase at the extension level we don't have 'XAML' access to it
            // try to find if the view model locator is already in the app resources dictionary
            if (System.Windows.Application.Current.TryFindResource("Locator") == null)
            {
                // instanciate the view model locator...
                ViewModelLocator = new ViewModelLocator();

                // ... and add it there
                System.Windows.Application.Current.Resources.Add("Locator", ViewModelLocator);
            }
        }

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();
            ViewModelLocator.DeviceExplorer.Package = this;
            DeviceExplorerCommand.Initialize(this, ViewModelLocator);
            DeployProvider.Initialize(this, ViewModelLocator);
        }
    }
}
