//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using CorDebugInterop;
using nanoFramework.Tools.Debugger;
using System.Diagnostics;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public class CorDebugClass : ICorDebugClass, ICorDebugClass2
    {
        private CorDebugAssembly _assembly;
        private Class _pdbxClass;
        private TypeSpec _pdbxTypeSpec;
        private uint _tkSymbolless;

        public CorDebugClass(CorDebugAssembly assembly, TypeSpec typeSpec)
        {
            _assembly = assembly;
            _pdbxTypeSpec = typeSpec;
            _pdbxClass = null;
        }

        public CorDebugClass(CorDebugAssembly assembly, Class cls)
        {
            _assembly = assembly;
            _pdbxClass = cls;
            _pdbxTypeSpec = null;
        }

        public CorDebugClass(CorDebugAssembly assembly, uint tkSymbolless)
        {
            _tkSymbolless = tkSymbolless;
            _assembly = assembly;
            _pdbxClass = null;
            _pdbxTypeSpec = null;
        }

        public ICorDebugClass ICorDebugClass
        {
            get { return (ICorDebugClass)this; }
        }

        public ICorDebugClass2 ICorDebugClass2
        {
            get { return (ICorDebugClass2)this; }
        }

        public CorDebugAssembly Assembly
        {
            [DebuggerHidden]
            get { return _assembly; }
        }

        public bool IsEnum
        {
            get
            {
                if (HasSymbols && _pdbxClass != null)
                {
                    return _pdbxClass.IsEnum;
                }
                else
                {
                    return false;
                }
            }
        }

        public Engine Engine
        {
            [DebuggerHidden]
            get { return Process.Engine; }
        }

        public CorDebugProcess Process
        {
            [DebuggerHidden]
            get { return Assembly.Process; }
        }

        public CorDebugAppDomain AppDomain
        {
            [DebuggerHidden]
            get { return Assembly.AppDomain; }
        }

        public Class PdbxClass
        {
            [DebuggerHidden]
            get { return _pdbxClass; }
        }

        public TypeSpec PdbxTypeSpec
        {
            [DebuggerHidden]
            get { return _pdbxTypeSpec; }
        }

        public bool HasSymbols
        {
            get { return (_pdbxClass != null || _pdbxTypeSpec != null); }
        }

        public uint TypeDef_Index
        {
            get
            {
                uint tk = _pdbxClass != null ? _pdbxClass.Token.NanoCLRToken : _tkSymbolless;

                return nanoCLR_TypeSystem.ClassMemberIndexFromnanoCLRToken(tk, Assembly);
            }
        }

        public uint TypeSpec_Index
        {
            get
            {
                uint tk = _pdbxTypeSpec != null ? _pdbxTypeSpec.Token.NanoCLRToken : _tkSymbolless;

                return nanoCLR_TypeSystem.ClassMemberIndexFromnanoCLRToken(tk, Assembly);
            }
        }

        // See CorDebug/CLAUDE.md "Consumer side".
        private CorDebugClass ForeignOpenTypeClass
        {
            get
            {
                if (_pdbxClass == null && _pdbxTypeSpec != null && _pdbxTypeSpec.GenericTypeDef == null &&
                    !string.IsNullOrEmpty(_pdbxTypeSpec.GenericTypeDefName))
                {
                    return _assembly.AppDomain.ClassFromFullName(_pdbxTypeSpec.GenericTypeDefName);
                }

                return null;
            }
        }

        #region ICorDebugClass Members

        int ICorDebugClass.GetModule(out ICorDebugModule pModule)
        {
            CorDebugClass openClass = ForeignOpenTypeClass;

            if (openClass != null)
            {
                return ((ICorDebugClass)openClass).GetModule(out pModule);
            }

            pModule = _assembly;

            return COM_HResults.S_OK;
        }

        int ICorDebugClass.GetToken(out uint pTypeDef)
        {
            // Must be a TypeDef token (IMetaDataImport has no TypeSpec lookup) -- never
            // _pdbxTypeSpec.Token. See CorDebug/CLAUDE.md "Consumer side".
            if (_pdbxClass != null)
            {
                pTypeDef = _pdbxClass.Token.CLRToken;
            }
            else if (_pdbxTypeSpec != null && _pdbxTypeSpec.GenericTypeDef != null)
            {
                pTypeDef = _pdbxTypeSpec.GenericTypeDef.CLRToken;
            }
            else
            {
                CorDebugClass openClass = ForeignOpenTypeClass;

                if (openClass != null)
                {
                    return ((ICorDebugClass)openClass).GetToken(out pTypeDef);
                }

                pTypeDef = _tkSymbolless;
            }

            return COM_HResults.S_OK;
        }

        int ICorDebugClass.GetStaticFieldValue(uint fieldDef, ICorDebugFrame pFrame, out ICorDebugValue ppValue)
        {
            //Cache, and invalidate when necessary???
            uint fd = nanoCLR_TypeSystem.ClassMemberIndexFromCLRToken(fieldDef, Assembly);
            Process.SetCurrentAppDomain(AppDomain);

            RuntimeValue rtv = Engine.GetStaticFieldValue(fd);
            ppValue = CorDebugValue.CreateValue(rtv, AppDomain);

            return COM_HResults.S_OK;
        }

        #endregion

        #region ICorDebugClass2 Members

        int ICorDebugClass2.GetParameterizedType(CorElementType elementType, uint nTypeArgs, ICorDebugType[] ppTypeArgs, out ICorDebugType ppType)
        {
            ppType = new CorDebugGenericType(elementType, null, Assembly);

            return COM_HResults.S_OK;
        }

        int ICorDebugClass2.SetJMCStatus(int bIsJustMyCode)
        {
            bool fJMC = Boolean.IntToBool(bIsJustMyCode);

            Debug.Assert(Utility.FImplies(fJMC, HasSymbols));

            int hres = fJMC ? COM_HResults.E_FAIL : COM_HResults.S_OK;

            if (HasSymbols)
            {
                if (Engine.Info_SetJMC(fJMC, ReflectionDefinition.Kind.REFLECTION_TYPE, TypeDef_Index))
                {
                    if (!_assembly.IsFrameworkAssembly)
                    {
                        //now update the debugger JMC state...
                        foreach (Method m in _pdbxClass.Methods)
                        {
                            m.IsJMC = fJMC;
                        }
                    }

                    hres = COM_HResults.S_OK;
                }
            }

            return hres;
        }

        #endregion
    }
}
