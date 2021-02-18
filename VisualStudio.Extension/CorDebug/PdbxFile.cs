//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public class PdbxFile : Pdbx
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
                get { return _assemblyDirectories; }
                set { _assemblyDirectories = value; }
            }

            public PdbxFile Resolve(string name, Tools.Debugger.WireProtocol.Commands.DebuggingResolveAssembly.Version version, bool fIsTargetBigEndian)
            {
                PdbxFile file = PdbxFile.Open(name, version, _assemblyPaths, _assemblyDirectories, fIsTargetBigEndian);

                return file;
            }

        }

        public string PdbxPath;

        private static PdbxFile TryPdbxFile(string path, Tools.Debugger.WireProtocol.Commands.DebuggingResolveAssembly.Version version)
        {
            try
            {
                path += ".pdbx";
                if (File.Exists(path))
                {
                    var pdbxContent = File.ReadAllText(path);

                    PdbxFile newFile = JsonSerializer.Deserialize<PdbxFile>(pdbxContent);

                    //Check version
                    var version2 = newFile.Assembly.Version;

                    if (version2.Major == version.MajorVersion && version2.Minor == version.MinorVersion)
                    {
                        newFile.Initialize(path);
                        return newFile;
                    }
                }
            }
            catch (Exception ex)
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

                if (!string.IsNullOrEmpty(directorySuffix))
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
                for (int iPath = 0; iPath < assemblyPaths.Length; iPath++)
                {
                    string path = assemblyPaths[iPath];
                    string pathNoExt = Path.ChangeExtension(path, null);

                    if (0 == string.Compare(name, Path.GetFileName(pathNoExt), true))
                    {
                        if ((file = TryPdbxFile(pathNoExt, version)) != null)
                        {
                            break;
                        }
                    }
                }
            }

            return file;
        }

        private void Initialize(string path)
        {
            PdbxPath = path;

            for (int iClass = 0; iClass < Assembly.Classes.Count; iClass++)
            {
                Class c = Assembly.Classes[iClass];
                c.Assembly = Assembly;

                for (int iMethod = 0; iMethod < c.Methods.Count; iMethod++)
                {
                    Method m = c.Methods[iMethod];
                    m.Class = c;
#if DEBUG
                    for (int iIL = 0; iIL < m.ILMap.Count - 1; iIL++)
                    {
                        Debug.Assert(m.ILMap[iIL].ClrToken < m.ILMap[iIL + 1].ClrToken);
                        Debug.Assert(m.ILMap[iIL].NanoClrToken < m.ILMap[iIL + 1].NanoClrToken);
                    }
#endif
                }

                foreach (Field f in c.Fields)
                {
                    f.Class = c;
                }
            }
        }
    }

    public partial class Token
    {
        public Token()
        {

        }

        public Token(uint clrToken, uint nanoClrToken)
        {
            Clr = clrToken.ToString("X8");
            NanoClr = nanoClrToken.ToString("X8");
        }

        public uint ClrToken => uint.Parse(Clr, System.Globalization.NumberStyles.HexNumber);
        public uint NanoClrToken => uint.Parse(NanoClr, System.Globalization.NumberStyles.HexNumber);
    }

    public partial class IL
    {
        public uint ClrToken
        {
            get 
            {
                return uint.Parse(Token.Clr, System.Globalization.NumberStyles.HexNumber);
            }

            set
            {
                if (Token == null)
                {
                    Token = new Token(value, 0);
                }
                else
                {
                    Token.Clr = value.ToString("X8");
                }
            }
        }

        public uint NanoClrToken
        {
            get
            {
                return uint.Parse(Token.NanoClr, System.Globalization.NumberStyles.HexNumber);
            }

            set
            {
                if (Token == null)
                {
                    Token = new Token(0, value);
                }
                else
                {
                    Token.NanoClr = value.ToString("X8");
                }
            }
        }
    }

    public class ClassMember : Token
    {
        public Class Class;
    }

    public partial class Method : ClassMember
    {
        private bool m_fIsJMC;

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

    public partial class Field : ClassMember
    {
    }

    public partial class Class
    {
        public Assembly Assembly;
    }

    public partial class Assembly
    {
        public CorDebugAssembly CorDebugAssembly;
    }
}
