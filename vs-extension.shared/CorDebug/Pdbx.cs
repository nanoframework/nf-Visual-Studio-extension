//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System.Diagnostics;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public class Pdbx
    {
        public class TokenMap
        {
            uint StringToUInt32(string s)
            {
                s.Trim();
                if (s.StartsWith("0x"))
                    s = s.Remove(0, 2);
                return uint.Parse(s, System.Globalization.NumberStyles.HexNumber);
            }

            string UInt32ToString(uint u)
            {
                return "0x" + u.ToString("X");
            }

            [XmlIgnore]public uint CLR;
            [XmlIgnore]public uint nanoCLR;

            [XmlElement("CLR")]
            public string CLR_String
            {
                get {return UInt32ToString(CLR);}
                set {CLR = StringToUInt32(value);}
            }

            [XmlElement("nanoCLR")]
            public string nanoCLR_String
            {
                get {return UInt32ToString(nanoCLR);}
                set {nanoCLR = StringToUInt32(value);}
            }
        }

        public class Token : TokenMap
        {
        }

        public class IL : TokenMap
        {
        }

        public class ClassMember
        {
            public Token Token;

            [XmlIgnore]public Class Class;
        }

        public class Method : ClassMember
        {
            public bool HasByteCode = true;

            public IL[] ILMap;

            private bool m_fIsJMC;

            [XmlIgnore]
            public bool IsJMC
            {
                get { return m_fIsJMC; }
                set { if (CanSetJMC) m_fIsJMC = value; }
            }

            public bool CanSetJMC
            {
                get { return HasByteCode; }
            }
        }

        public class Field : ClassMember
        {
        }

        public class Class
        {
            public Token Token;
            public Field[] Fields;
            public Method[] Methods;
            [XmlIgnore]public Assembly Assembly;
        }

        public class Assembly   /*Module*/
        {
            public struct VersionStruct
            {
                public ushort Major;
                public ushort Minor;
                public ushort Build;
                public ushort Revision;
            }

            public string FileName;
            public VersionStruct Version;
            public Token Token;
            public Class[] Classes;
            [XmlIgnore]public CorDebugAssembly CorDebugAssembly;
        }

        public class PdbxFile
        {
            public class Resolver
            {
                private string[] _assemblyPaths;
                private string[] _assemblyDirectories;

                public string[] AssemblyPaths
                {
                    get { return _assemblyPaths; }
                    set { _assemblyPaths = value; }
                }

                public string[] AssemblyDirectories
                {
                    get {return _assemblyDirectories;}
                    set {_assemblyDirectories = value;}
                }

                public PdbxFile Resolve(string name, Tools.Debugger.WireProtocol.Commands.DebuggingResolveAssembly.Version version, bool fIsTargetBigEndian)
                {
                    PdbxFile file = PdbxFile.Open(name, version, _assemblyPaths, _assemblyDirectories, fIsTargetBigEndian);

                    return file;
                }

            }

            public Assembly Assembly;
            [XmlIgnore]public string PdbxPath;

            private static PdbxFile TryPdbxFile(string path, Tools.Debugger.WireProtocol.Commands.DebuggingResolveAssembly.Version version)
            {
                try
                {
                    path += ".pdbx";
                    if (File.Exists(path))
                    {
                        XmlSerializer xmls = new Serialization.PdbxFile.PdbxFileSerializer();

                        PdbxFile file = (PdbxFile)Utility.XmlDeserialize(path, xmls);

                        //Check version
                        Assembly.VersionStruct version2 = file.Assembly.Version;

                        if (version2.Major == version.MajorVersion && version2.Minor == version.MinorVersion)
                        {
                            file.Initialize(path);
                            return file;
                        }
                    }
                }
                catch
                {
                }

                return null;
            }

            private static PdbxFile OpenHelper(string name, Tools.Debugger.WireProtocol.Commands.DebuggingResolveAssembly.Version version, string[] assemblyDirectories, string directorySuffix)
            {
                PdbxFile file = null;

                for (int iDirectory = 0; iDirectory < assemblyDirectories.Length; iDirectory++)
                {
                    string directory = assemblyDirectories[iDirectory];

                    if(!string.IsNullOrEmpty(directorySuffix))
                    {
                        directory = Path.Combine(directory, directorySuffix);
                    }

                    string pathNoExt = Path.Combine(directory, name);

                    if ((file = TryPdbxFile(pathNoExt, version)) != null)
                        break;
                }

                return file;
            }

            private static PdbxFile Open(string name, Tools.Debugger.WireProtocol.Commands.DebuggingResolveAssembly.Version version, string[] assemblyPaths, string[] assemblyDirectories, bool fIsTargetBigEndian)
            {
                PdbxFile file = null;

                if (assemblyPaths != null)
                {
                    for(int iPath = 0; iPath < assemblyPaths.Length; iPath++)
                    {
                        string path = assemblyPaths[iPath];
                        string pathNoExt = Path.ChangeExtension(path, null);

                        if (0 == string.Compare(name, Path.GetFileName(pathNoExt), true))
                        {
                            if ((file = TryPdbxFile(pathNoExt, version)) != null)
                                break;
                        }
                    }
                }

                if (file == null && assemblyDirectories != null)
                {
                    file = OpenHelper(name, version, assemblyDirectories, null);

                    if (file == null)
                    {
                        if (fIsTargetBigEndian)
                        {
                            file = OpenHelper(name, version, assemblyDirectories, @"..\pe\be");

                            if (file == null)
                            {
                                file = OpenHelper(name, version, assemblyDirectories, @"be");
                            }
                        }
                        else
                        {
                            file = OpenHelper(name, version, assemblyDirectories, @"..\pe\le");

                            if (file == null)
                            {
                                file = OpenHelper(name, version, assemblyDirectories, @"le");
                            }
                        }
                    }
                }

                //Try other paths here...
                return file;
            }

            private void Initialize(string path)
            {
                PdbxPath = path;

                for(int iClass = 0; iClass < Assembly.Classes.Length; iClass++)
                {
                    Class c = Assembly.Classes[iClass];
                    c.Assembly = Assembly;

                    for(int iMethod = 0; iMethod < c.Methods.Length; iMethod++)
                    {
                        Method m = c.Methods[iMethod];
                        m.Class = c;
#if DEBUG
                        for (int iIL = 0; iIL < m.ILMap.Length - 1; iIL++)
                        {
                            Debug.Assert(m.ILMap[iIL].CLR < m.ILMap[iIL + 1].CLR);
                            Debug.Assert(m.ILMap[iIL].nanoCLR < m.ILMap[iIL + 1].nanoCLR);
                        }
#endif
                    }

                    foreach (Field f in c.Fields)
                    {
                        f.Class = c;
                    }
                }
            }

            /// Format of the Pdbx file
            ///
            ///<Pdbx>
            /// <dat>
            ///  <filename>NAME</filename>
            /// </dat>
            ///  <assemblies>
            ///   <assembly>
            ///    <name>NAME</name>
            ///    <version>
            ///     <Major>1</Major>
            ///     <Minor>2</Minor>
            ///     <Build>3</Build>
            ///     <Revision>4</Revision>
            ///    </version>
            ///     <token>
            ///      <CLR>TOKEN</CLR>
            ///      <nanoCLR>TOKEN</nanoCLR>
            ///     </token>
            ///     <classes>
            ///      <class>
            ///       <name>NAME</name>
            ///       <fields>
            ///         <field>
            ///          <token></token>
            ///         </field>
            ///       </fields>
            ///       <methods>
            ///        <method>
            ///          <token></token>
            ///          <ILMap>
            ///            <IL>
            ///             <CLR>IL</CLR>
            ///             <nanoCLR>IL</nanoCLR>
            ///            </IL>
            ///          </ILMap>
            ///        </method>
            ///       </methods>
            ///      </class>
            ///     </classes>
            ///   </assembly>
            ///  </assemblies>
            ///</Pdbx>
            ///
            ///
        }
    }
}