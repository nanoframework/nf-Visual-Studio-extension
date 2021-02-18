//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using CorDebugInterop;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.VisualStudio.Extension.MetaData;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public class CorDebugAssembly : ICorDebugAssembly, ICorDebugModule, ICorDebugModule2, IDisposable
    {
        CorDebugAppDomain _appDomain;
        CorDebugProcess _process;
        Hashtable _htTokenCLRToPdbx;
        Hashtable _htTokennanoCLRToPdbx;
        PdbxFile _pdbxFile;
        Assembly _pdbxAssembly;
        IMetaDataImport _iMetaDataImport;
        uint _idx;
        string _name;
        string _path;
        ulong _dummyBaseAddress;
        FileStream _fileStream;
        CorDebugAssembly _primaryAssembly;
        bool _isFrameworkAssembly;

        // this list holds the official assemblies name
        List<string> frameworkAssemblies_v1_0 = new List<string> {
            "mscorlib",
            "nanoframework.hardware.esp32",
            "nanoframework.networking.sntp",
            "nanoframework.runtime.events",
            "nanoframework.runtime.native",
            "windows.devices.adc",
            "windows.devices.gpio",
            "windows.devices.i2c",
            "windows.devices.pwm",
            "windows.devices.serialcommunication",
            "windows.devices.spi",
            "windows.networking.sockets",
            "windows.storage.streams",
            "system.net",
        };



        public CorDebugAssembly( CorDebugProcess process, string name, PdbxFile pdbxFile, uint idx )
        {
            _process = process;
            _appDomain = null;
            _name = name;
            _pdbxFile = pdbxFile;
            _pdbxAssembly = (pdbxFile != null) ? pdbxFile.Assembly : null;
            _htTokenCLRToPdbx = new Hashtable();
            _htTokennanoCLRToPdbx = new Hashtable();
            _idx = idx;
            _primaryAssembly = null;
            _isFrameworkAssembly = false;

            if(_pdbxAssembly != null)
            {
                if (!string.IsNullOrEmpty(pdbxFile.PdbxPath))
                {
                    string pdbxPath = pdbxFile.PdbxPath.ToLower();

                    // pdbx files are supposed to be in the 'packages' folder
                    if (pdbxPath.Contains(@"\packages\"))
                    {

                        _isFrameworkAssembly = (frameworkAssemblies_v1_0.Contains(name.ToLower()));
                    }
                }

                _pdbxAssembly.CorDebugAssembly = this;

                foreach(Class c in _pdbxAssembly.Classes)
                {
                    AddTokenToHashtables( c.Token, c );
                    foreach(Field field in c.Fields)
                    {
                        AddTokenToHashtables( field.Token, field );
                    }

                    foreach(Method method in c.Methods)
                    {
                        AddTokenToHashtables( method.Token, method );
                    }
                }
            }
        }

        public ICorDebugAssembly ICorDebugAssembly
        {
            get { return ((ICorDebugAssembly)this); }
        }

        public ICorDebugModule ICorDebugModule
        {
            get { return ((ICorDebugModule)this); }
        }

        private bool IsPrimaryAssembly
        {
            get { return _primaryAssembly == null; }
        }

        public bool IsFrameworkAssembly
        {
            get { return _isFrameworkAssembly; }
        }

        private FileStream EnsureFileStream()
        {
            if(IsPrimaryAssembly)
            {
                if(_path != null && _fileStream == null)
                {
                    _fileStream = File.OpenRead( _path );

                    _dummyBaseAddress = _process.FakeLoadAssemblyIntoMemory( this );
                }

                return _fileStream;
            }
            else
            {
                FileStream fileStream = _primaryAssembly.EnsureFileStream();
                _dummyBaseAddress = _primaryAssembly._dummyBaseAddress;

                return fileStream;
            }
        }
        
        public CorDebugAssembly CreateAssemblyInstance( CorDebugAppDomain appDomain )
        {
            //Ensure the metadata import is created.  
            IMetaDataImport iMetaDataImport = MetaDataImport;

            CorDebugAssembly assm = (CorDebugAssembly)MemberwiseClone();
            assm._appDomain = appDomain;
            assm._primaryAssembly = this;

            return assm;
        }

        internal void ReadMemory( ulong address, uint size, byte[] buffer, out uint read )
        {
            FileStream fileStream = EnsureFileStream();

            read = 0;

            if(fileStream != null)
            {
                lock(fileStream)
                {
                    fileStream.Position = (long)address;
                    read = (uint)fileStream.Read( buffer, 0, (int)size );
                }
            }
        }

        public static CorDebugAssembly AssemblyFromIdx( uint idx, ArrayList assemblies )
        {
            foreach(CorDebugAssembly assembly in assemblies)
            {
                if(assembly.Idx == idx)
                    return assembly;
            }
            return null;
        }

        public static CorDebugAssembly AssemblyFromIndex( uint index, ArrayList assemblies )
        {
            return AssemblyFromIdx( nanoCLR_TypeSystem.IdxAssemblyFromIndex( index ), assemblies );
        }

        public string Name
        {
            get { return _name; }
        }

        public bool HasSymbols
        {
            get { return _pdbxAssembly != null; }
        }

        private void AddTokenToHashtables(Token token, object o)
        {
            _htTokenCLRToPdbx[token.ClrToken] = o;
            _htTokennanoCLRToPdbx[token.NanoClrToken] = o;
        }

        private string FindAssemblyOnDisk()
        {
            if (_path == null && _pdbxAssembly != null)
            {

                string[] pathsToTry = new string[]
                {
                    // Look next to pdbx file
                    Path.Combine( Path.GetDirectoryName( _pdbxFile.PdbxPath ), _pdbxAssembly.FileName ),
                };

                for (int iPath = 0; iPath < pathsToTry.Length; iPath++)
                {
                    string path = pathsToTry[iPath];

                    if (File.Exists(path))
                    {
                        //is this the right file?
                        _path = path;
                        break;
                    }
                }
            }

            return _path;
        }

        private IMetaDataImport FindMetadataImport()
        {
            Debug.Assert( _iMetaDataImport == null );

            IMetaDataDispenser mdd = new CorMetaDataDispenser() as IMetaDataDispenser;
            object pImport = null;
            Guid iid = typeof( IMetaDataImport ).GUID;
            IMetaDataImport metaDataImport = null;

            try
            {
                string path = FindAssemblyOnDisk();

                if(path != null)
                {
                    mdd.OpenScope( path, (int)MetaData.CorOpenFlags.ofRead, ref iid, out pImport );
                    metaDataImport = pImport as IMetaDataImport;
                }
            }
            catch
            {
            }

            //check the version?
            return metaDataImport;
        }

        public IMetaDataImport MetaDataImport
        {
            get
            {
                if(_iMetaDataImport == null)
                {
                    if(HasSymbols)
                    {
                        _iMetaDataImport = FindMetadataImport();
                    }

                    if(_iMetaDataImport == null)
                    {
                        _pdbxFile = null;
                        _pdbxAssembly = null;
                        _iMetaDataImport = new MetaDataImport( this );
                    }
                }

                return _iMetaDataImport;
            }
        }

        public CorDebugProcess Process
        {
            [DebuggerHidden]
            get { return _process; }
        }

        public CorDebugAppDomain AppDomain
        {
            [DebuggerHidden]
            get { return _appDomain; }
        }

        public uint Idx
        {
            [DebuggerHidden]
            get { return _idx; }
        }

        private CorDebugFunction GetFunctionFromToken( uint tk, Hashtable ht )
        {
            CorDebugFunction function = null;
            Method method = ht[tk] as Method;
            if(method != null)
            {
                CorDebugClass c = new CorDebugClass( this, method.Class );
                function = new CorDebugFunction( c, method );
            }

            Debug.Assert( function != null );
            return function;
        }

        public CorDebugFunction GetFunctionFromTokenCLR( uint tk )
        {
            return GetFunctionFromToken( tk, _htTokenCLRToPdbx );
        }

        public CorDebugFunction GetFunctionFromTokennanoCLR( uint tk )
        {
            if(HasSymbols)
            {
                return GetFunctionFromToken( tk, _htTokennanoCLRToPdbx );
            }
            else
            {
                uint index = nanoCLR_TypeSystem.ClassMemberIndexFromnanoCLRToken( tk, this );

                Debugger.WireProtocol.Commands.Debugging_Resolve_Method.Result resolvedMethod = Process.Engine.ResolveMethod(index);
                Debug.Assert( nanoCLR_TypeSystem.IdxAssemblyFromIndex( resolvedMethod.m_td ) == Idx);

                uint tkMethod = nanoCLR_TypeSystem.SymbollessSupport.MethodDefTokenFromnanoCLRToken( tk );
                uint tkClass = nanoCLR_TypeSystem.nanoCLRTokenFromTypeIndex( resolvedMethod.m_td );

                CorDebugClass c = GetClassFromTokennanoCLR( tkClass );

                return new CorDebugFunction( c, tkMethod );
            }
        }

        public ClassMember GetPdbxClassMemberFromTokenCLR( uint tk )
        {
            return _htTokenCLRToPdbx[tk] as ClassMember;
        }

        private CorDebugClass GetClassFromToken( uint tk, Hashtable ht )
        {
            CorDebugClass cls = null;
            Class c = ht[tk] as Class;
            if(c != null)
            {
                cls = new CorDebugClass( this, c );
            }

            return cls;
        }

        public CorDebugClass GetClassFromTokenCLR( uint tk )
        {
            return GetClassFromToken( tk, _htTokenCLRToPdbx );
        }

        public CorDebugClass GetClassFromTokennanoCLR( uint tk )
        {
            if(HasSymbols)
                return GetClassFromToken( tk, _htTokennanoCLRToPdbx );
            else
                return new CorDebugClass( this, nanoCLR_TypeSystem.SymbollessSupport.TypeDefTokenFromnanoCLRToken( tk ) );
        }

        ~CorDebugAssembly()
        {
            try
            {
                ((IDisposable)this).Dispose();
            }
            catch(Exception)
            {
            }
        }

        #region IDisposable Members

        void IDisposable.Dispose()
        {
            if(IsPrimaryAssembly)
            {
                if(_iMetaDataImport != null && !(_iMetaDataImport is MetaDataImport))
                {
                    Marshal.ReleaseComObject( _iMetaDataImport );
                }

                _iMetaDataImport = null;

                if(_fileStream != null)
                {
                    ((IDisposable)_fileStream).Dispose();
                    _fileStream = null;
                }
            }

            GC.SuppressFinalize( this );
        }

        #endregion

        #region ICorDebugAssembly Members

        int ICorDebugAssembly.GetProcess( out ICorDebugProcess ppProcess )
        {
            ppProcess = Process;

            return COM_HResults.S_OK;            
        }

        int ICorDebugAssembly.GetAppDomain( out ICorDebugAppDomain ppAppDomain )
        {
            ppAppDomain = _appDomain;

            return COM_HResults.S_OK;            
        }

        int ICorDebugAssembly.EnumerateModules( out ICorDebugModuleEnum ppModules )
        {
            ppModules = new CorDebugEnum( this, typeof( ICorDebugModule ), typeof( ICorDebugModuleEnum ) );

            return COM_HResults.S_OK;            
        }

        int ICorDebugAssembly.GetCodeBase( uint cchName, IntPtr pcchName, IntPtr szName )
        {
            Utility.MarshalString( "", cchName, pcchName, szName );

            return COM_HResults.S_OK;            
        }

        int ICorDebugAssembly.GetName( uint cchName, IntPtr pcchName, IntPtr szName )
        {
            string name = _path != null ? _path : _name;

            Utility.MarshalString( name, cchName, pcchName, szName );

            return COM_HResults.S_OK;         
        }

        #endregion

        #region ICorDebugModule Members

        int ICorDebugModule.GetProcess( out ICorDebugProcess ppProcess )
        {
            ppProcess = Process;

            return COM_HResults.S_OK;            
        }

        int ICorDebugModule.GetBaseAddress( out ulong pAddress )
        {
            EnsureFileStream();
            pAddress = _dummyBaseAddress;

            return COM_HResults.S_OK;            
        }

        int ICorDebugModule.GetAssembly( out ICorDebugAssembly ppAssembly )
        {
            ppAssembly = this;

            return COM_HResults.S_OK;
        }

        int ICorDebugModule.GetName( uint cchName, IntPtr pcchName, IntPtr szName )
        {
            return ICorDebugAssembly.GetName( cchName, pcchName, szName );            
        }

        int ICorDebugModule.EnableJITDebugging( int bTrackJITInfo, int bAllowJitOpts )
        {
            return COM_HResults.S_OK;
        }

        int ICorDebugModule.EnableClassLoadCallbacks( int bClassLoadCallbacks )
        {
            return COM_HResults.S_OK;
        }

        int ICorDebugModule.GetFunctionFromToken( uint methodDef, out ICorDebugFunction ppFunction )
        {
            ppFunction = GetFunctionFromTokenCLR( methodDef );

            return COM_HResults.S_OK;
        }

        int ICorDebugModule.GetFunctionFromRVA( ulong rva, out ICorDebugFunction ppFunction )
        {
            ppFunction = null;

            return COM_HResults.S_OK;
        }

        int ICorDebugModule.GetClassFromToken( uint typeDef, out ICorDebugClass ppClass )
        {
            ppClass = GetClassFromTokenCLR( typeDef );

            return COM_HResults.S_OK;
        }

        int ICorDebugModule.CreateBreakpoint( out ICorDebugModuleBreakpoint ppBreakpoint )
        {
            ppBreakpoint = null;

            return COM_HResults.E_NOTIMPL;
        }

        int ICorDebugModule.GetEditAndContinueSnapshot( out ICorDebugEditAndContinueSnapshot ppEditAndContinueSnapshot )
        {
            ppEditAndContinueSnapshot = null;

            return COM_HResults.S_OK;
        }

        int ICorDebugModule.GetMetaDataInterface( ref Guid riid, out IntPtr ppObj )
        {
            IntPtr pMetaDataImport = Marshal.GetIUnknownForObject(MetaDataImport);

            Marshal.QueryInterface( pMetaDataImport, ref riid, out ppObj );
            int cRef = Marshal.Release( pMetaDataImport );

            Debug.Assert( riid == typeof( IMetaDataImport ).GUID || riid == typeof( IMetaDataImport2 ).GUID || riid == typeof( IMetaDataAssemblyImport ).GUID );
            Debug.Assert(MetaDataImport != null && ppObj != IntPtr.Zero );

            return COM_HResults.S_OK;
        }

        int ICorDebugModule.GetToken( out uint pToken )
        {
            pToken = _pdbxAssembly.Token.ClrToken;

            return COM_HResults.S_OK;
        }

        int ICorDebugModule.IsDynamic( out int pDynamic )
        {
            pDynamic = Boolean.FALSE;

            return COM_HResults.S_OK;
        }

        int ICorDebugModule.GetGlobalVariableValue( uint fieldDef, out ICorDebugValue ppValue )
        {
            ppValue = null;

            return COM_HResults.S_OK;
        }

        int ICorDebugModule.GetSize( out uint pcBytes )
        {
            pcBytes = 0x1000;

            FileStream fileStream = EnsureFileStream();

            if(fileStream != null)
            {
                pcBytes = (uint)fileStream.Length;
            }

            return COM_HResults.S_OK;
        }

        int ICorDebugModule.IsInMemory( out int pInMemory )
        {
            pInMemory = Boolean.BoolToInt( !HasSymbols );// Boolean.FALSE;

            return COM_HResults.S_OK;
        }

        #endregion

        #region ICorDebugModule2 Members

        int ICorDebugModule2.SetJMCStatus( int bIsJustMyCode, uint cTokens, ref uint pTokens )
        {
            Debug.Assert(cTokens == 0);

            bool fJMC = Boolean.IntToBool( bIsJustMyCode );

            int hres = fJMC ? COM_HResults.E_FAIL : COM_HResults.S_OK;

            Debug.Assert( Utility.FImplies( fJMC, HasSymbols) );

            if (HasSymbols)
            {
                if (Process.Engine.Info_SetJMC(fJMC, ReflectionDefinition.Kind.REFLECTION_ASSEMBLY, nanoCLR_TypeSystem.IndexFromIdxAssemblyIdx(Idx)))
                {
                    if(!_isFrameworkAssembly)
                    {
                        //now update the debugger JMC state...
                        foreach (Class c in _pdbxAssembly.Classes)
                        {
                            foreach (Method m in c.Methods)
                            {
                                m.IsJMC = fJMC;
                            }
                        }
                    }
                    hres = COM_HResults.S_OK;
                }
            }

            return hres;
        }

        int ICorDebugModule2.ApplyChanges( uint cbMetadata, byte[] pbMetadata, uint cbIL, byte[] pbIL )
        {
            return COM_HResults.S_OK;
        }

        int ICorDebugModule2.SetJITCompilerFlags( uint dwFlags )
        {
            return COM_HResults.S_OK;
        }

        int ICorDebugModule2.GetJITCompilerFlags( out uint pdwFlags )
        {
            pdwFlags = (uint)CorDebugJITCompilerFlags.CORDEBUG_JIT_DISABLE_OPTIMIZATION;

            return COM_HResults.S_OK;
        }

        int ICorDebugModule2.ResolveAssembly( uint tkAssemblyRef, out ICorDebugAssembly ppAssembly )
        {
            ppAssembly = null;
            return COM_HResults.S_OK;
        }

        #endregion
    }
}
