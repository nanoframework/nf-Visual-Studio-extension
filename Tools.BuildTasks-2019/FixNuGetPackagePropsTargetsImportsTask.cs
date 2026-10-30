// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace nanoFramework.Tools
{
    [Description("FixNuGetPackagePropsTargetsImportsTaskEntry")]

    public class FixNuGetPackagePropsTargetsImportsTask : Task
    {
        #region public properties for the task
        [Required]
        public string ProjectFile { get; set; }

        #endregion

        #region Fields
        private string _projectDirectory;
        private readonly Dictionary<string, string> _packageVersionsToVerify = new Dictionary<string, string>();
        private XDocument _nfProject;
        private bool _nfProjectIsChanged;
        private string _packageDirectory;
        private readonly List<string> _importedFiles = new List<string>();
        #endregion

        public override bool Execute()
        {
            _projectDirectory = Path.GetDirectoryName(Path.GetFullPath(ProjectFile));
            string packageConfigPath = Path.Combine(_projectDirectory, "packages.config");
            if (!File.Exists(packageConfigPath))
            {
                return true;
            }

            // report to VS output window what step the build is 
            Log.LogMessage(MessageImportance.Normal, "Verifying NuGet package references...");

            if (!ReadXmlFiles(packageConfigPath))
            {
                return true;
            }

            if (!GetPackageDirectory())
            {
                Log.LogMessage(MessageImportance.Normal, "NuGet package directory cannot be determined; skipping import verification.");
            }

            VerifyExistingImports();

            AddImportsForPropsAndTargets();

            if (_nfProjectIsChanged)
            {
                ReplaceTarget();
                SaveUpdatedProject();
                return false;
            }
            else
            {
                return true;
            }
        }

        private bool ReadXmlFiles(string packageConfigPath)
        {
            // Read the packages from packages.config
            _packageVersionsToVerify.Clear();
            try
            {
                XElement packageRoot = XDocument.Load(packageConfigPath).Root;
                if (!(packageRoot is null))
                {
                    foreach (XElement package in packageRoot.Elements("package"))
                    {
                        _packageVersionsToVerify[package.Attribute("id").Value] = package.Attribute("version").Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogError(null, null, null, packageConfigPath, 0, 0, 0, 0, $"Cannot read the file: {ex}");
                return false;
            }

            // Read the project file
            try
            {
                _nfProject = XDocument.Load(ProjectFile);
                _nfProjectIsChanged = false;
            }
            catch (Exception ex)
            {
                Log.LogError(null, null, null, ProjectFile, 0, 0, 0, 0, $"Cannot read the file: {ex}");
                return false;
            }

            return true;
        }

        private bool GetPackageDirectory()
        {
            _packageDirectory = null;

            bool IsPackageDirectoryPath(string path)
            {
                if (!(path is null)
                        && File.Exists(Path.Combine(_projectDirectory, path)))
                {
                    foreach (KeyValuePair<string, string> package in _packageVersionsToVerify)
                    {
                        int idx = path.IndexOf($"{Path.DirectorySeparatorChar}{package.Key}.{package.Value}{Path.DirectorySeparatorChar}");
                        if (idx >= 0)
                        {
                            _packageDirectory = path.Substring(0, idx + 1);
                            return true;
                        }
                    }
                }

                return false;
            }

            // All (regular) projects must have a reference to mscorlib. Find that first.
            foreach (XElement itemGroup in _nfProject.Root.Elements(_nfProject.Root.Name.Namespace + "ItemGroup"))
            {
                foreach (XElement reference in itemGroup.Elements(_nfProject.Root.Name.Namespace + "Reference"))
                {
                    string path = reference.Element(_nfProject.Root.Name.Namespace + "HintPath")?.Value;
                    if (IsPackageDirectoryPath(path))
                    {
                        return true;
                    }
                }
            }

            // Maybe the imports provide a clue?
            foreach (XElement import in _nfProject.Root.Elements(_nfProject.Root.Name.Namespace + "Import"))
            {
                string path = import.Attribute("Project")?.Value;
                if (IsPackageDirectoryPath(path))
                {
                    return true;
                }
            }

            return false;
        }

        private void VerifyExistingImports()
        {
            var packagesFound = new HashSet<string>();
            _importedFiles.Clear();

            foreach (XElement import in _nfProject.Root.Elements(_nfProject.Root.Name.Namespace + "Import").ToList())
            {
                string path = import.Attribute("Project")?.Value;
                if (!(path is null)
                    && path.StartsWith(_packageDirectory))
                {
                    bool packageStillRequired = false;
                    if (File.Exists(Path.Combine(_projectDirectory, path)))
                    {
                        foreach (KeyValuePair<string, string> package in _packageVersionsToVerify)
                        {
                            if (path.Substring(_packageDirectory.Length).StartsWith($"{package.Key}.{package.Value}{Path.DirectorySeparatorChar}"))
                            {
                                packagesFound.Add(package.Key);
                                packageStillRequired = true;
                                break;
                            }
                        }
                    }

                    if (packageStillRequired)
                    {
                        _importedFiles.Add(path);
                    }
                    else
                    {
                        import.Remove();
                        _nfProjectIsChanged = true;
                    }
                }
            }

            foreach (string package in packagesFound)
            {
                _packageVersionsToVerify.Remove(package);
            }
        }

        private void AddImportsForPropsAndTargets()
        {
            foreach (KeyValuePair<string, string> package in _packageVersionsToVerify)
            {
                string buildBasePath = Path.Combine(_packageDirectory, $"{package.Key}.{package.Value}", "build", package.Key);
                if (File.Exists(Path.Combine(_projectDirectory, $"{buildBasePath}.props")))
                {
                    _importedFiles.Add($"{buildBasePath}.props");
                    _nfProject.Root.AddFirst(new XElement(
                        _nfProject.Root.Name.Namespace + "Import",
                        new XAttribute("Project", $"{buildBasePath}.props"),
                        new XAttribute("Condition", $"Exists('{buildBasePath}.props')")
                    ));
                    _nfProjectIsChanged = true;
                }

                if (File.Exists(Path.Combine(_projectDirectory, $"{buildBasePath}.targets")))
                {
                    _importedFiles.Add($"{buildBasePath}.targets");
                    _nfProject.Root.Add(new XElement(
                        _nfProject.Root.Name.Namespace + "Import",
                        new XAttribute("Project", $"{buildBasePath}.targets"),
                        new XAttribute("Condition", $"Exists('{buildBasePath}.targets')")
                    ));
                    _nfProjectIsChanged = true;
                }
            }
        }

        private void ReplaceTarget()
        {
            XElement target = (from t in _nfProject.Root.Elements(_nfProject.Root.Name.Namespace + "Target")
                               where t.Attribute("Name")?.Value == "EnsureNuGetPackageBuildImports"
                               select t).FirstOrDefault();
            target?.Remove();

            if (_importedFiles.Count > 0)
            {
                target = new XElement(_nfProject.Root.Name.Namespace + "Target",
                    new XAttribute("Name", "EnsureNuGetPackageBuildImports"),
                    new XAttribute("BeforeTargets", "PrepareForBuild"),
                    new XElement(_nfProject.Root.Name.Namespace + "PropertyGroup",
                        new XElement(_nfProject.Root.Name.Namespace + "ErrorText",
                            "This project references NuGet package(s) that are missing on this computer. Enable NuGet Package Restore to download them.  For more information, see http://go.microsoft.com/fwlink/?LinkID=322105.The missing file is {0}."
                        )
                    )
                );

                _nfProject.Root.Add(target);

                foreach (string path in from p in _importedFiles
                                        orderby p
                                        select p)
                {
                    target.Add(new XElement(_nfProject.Root.Name.Namespace + "Error",
                        new XAttribute("Condition", $"!Exists('{path}')"),
                        new XAttribute("Text", $"$([System.String]::Format('$(ErrorText)', '{path}'))")
                    ));
                }
            }
        }

        private void SaveUpdatedProject()
        {
            try
            {
                _nfProject.Save(ProjectFile);
            }
            catch (Exception ex)
            {
                Log.LogError(null, null, null, ProjectFile, 0, 0, 0, 0, $"Cannot update the project file: {ex}");
                return;
            }

            Log.LogError(null, null, null, ProjectFile, 0, 0, 0, 0, "The project file has been updated; restart the build.");
        }
    }
}
